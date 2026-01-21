#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.Infrastructure.Logging;
using Runtime.Localization;
using Runtime.UI.Core;
using Runtime.UI.Components;

namespace Runtime.GameModes.Wizard
{
    /// <summary>
    /// View-model for the match setup step of the game mode wizard.
    /// Synchronizes session state and owns the active mode-specific settings view model.
    /// </summary>
    public sealed class MatchSetupViewModel : BaseViewModel
    {
        private static readonly string[] InlineErrorPriority =
        {
            "SelectedModeId",
            "ModeConfig",
            "BotDifficultyId",
            "TargetPlayerId",
            "Matchmaking",
            "ModeCatalog"
        };

        private readonly IGameModeCatalog _catalog;
        private readonly IGameModeWizardCoordinator _coordinator;
        private readonly ILocalizationService _localization;
        private readonly IBotDifficultyCatalog _difficultyCatalog;

        private readonly ReactiveProperty<string> _modeTitleText = new(string.Empty);
        private readonly ReactiveProperty<string> _modeIconKey = new(string.Empty);
        private readonly ReactiveProperty<ModeSettingsPresentation?> _activeSettings = new(null);
        private readonly ReactiveProperty<OpponentType> _opponentType = new(global::Runtime.GameModes.Wizard.OpponentType.Bot);
        private readonly ReactiveProperty<IReadOnlyList<BotDifficulty>> _availableDifficulties;
        private readonly ReactiveProperty<IReadOnlyList<DifficultyChipItem>> _difficultyItems = new(Array.Empty<DifficultyChipItem>());
        private readonly ReactiveProperty<string?> _selectedDifficultyId = new(null);
        private readonly ReactiveProperty<bool> _isBotSettingsVisible = new(true);
        private readonly ReactiveProperty<bool> _canStart = new(false);
        private readonly ReactiveProperty<bool> _isBusy = new(false);
        private readonly ReactiveProperty<string?> _inlineErrorText = new(null);

        private IGameModeSession? _session;
        private string? _activeModeId;

        private bool _sessionCanStart;
        private int _lastAppliedVersion;

        private IDisposable? _modeTitleSubscription;
        private CompositeDisposable? _difficultyLocalizationSubscriptions;
        private readonly Dictionary<string, string> _difficultyLabels = new(StringComparer.Ordinal);
        private string? _validationErrorText;
        private string? _coordinatorInlineErrorText;

        private int _difficultyItemsRebuildScheduled;
        private int _difficultyItemsRebuildVersion;

        private ISpecificModeSettingsViewModel? _activeSettingsViewModel;
        private IDisposable? _activeConfigSubscription;

        private int _isWired;
        // Protects against feedback loop: UI -> session -> UI
        private int _isSyncingFromSession;
        // Protects against feedback loop: UI -> session -> UI
        private int _isSyncingDifficultyFromSession;

        public ReadOnlyReactiveProperty<string> ModeTitleText => _modeTitleText;
        public ReadOnlyReactiveProperty<string> ModeIconKey => _modeIconKey;
        public ReadOnlyReactiveProperty<ModeSettingsPresentation?> ActiveSettings => _activeSettings;
        public ReactiveProperty<OpponentType> OpponentType => _opponentType;
        public ReadOnlyReactiveProperty<IReadOnlyList<BotDifficulty>> AvailableDifficulties => _availableDifficulties;
        public ReadOnlyReactiveProperty<IReadOnlyList<DifficultyChipItem>> DifficultyItems => _difficultyItems;
        public ReactiveProperty<string?> SelectedDifficultyId => _selectedDifficultyId;
        public ReadOnlyReactiveProperty<bool> IsBotSettingsVisible => _isBotSettingsVisible;
        public ReadOnlyReactiveProperty<bool> CanStart => _canStart;
        public ReadOnlyReactiveProperty<bool> IsBusy => _isBusy;
        public ReadOnlyReactiveProperty<WizardError?> Error => _coordinator.CurrentError;
        public ReadOnlyReactiveProperty<string?> InlineErrorText => _inlineErrorText;

        public Observable<string> BackButtonText { get; }
        public Observable<string> CancelButtonText { get; }
        public Observable<string> StartButtonText { get; }
        public Observable<string> OpponentBotText { get; }
        public Observable<string> OpponentHumanText { get; }
        public Observable<string> OpponentSectionTitle { get; }
        public Observable<string> ModeOptionsTitle { get; }
        public Observable<string> BotDifficultyTitle { get; }

        public MatchSetupViewModel(
            IGameModeCatalog catalog,
            IGameModeWizardCoordinator coordinator,
            ILocalizationService localization,
            IBotDifficultyCatalog difficultyCatalog)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
            _localization = localization ?? throw new ArgumentNullException(nameof(localization));
            _difficultyCatalog = difficultyCatalog ?? throw new ArgumentNullException(nameof(difficultyCatalog));

            _availableDifficulties = new ReactiveProperty<IReadOnlyList<BotDifficulty>>(
                _difficultyCatalog.Difficulties ?? throw new ArgumentException("Difficulty catalog returned null list.", nameof(difficultyCatalog)));

            var table = new TextTableId("GameModeWizard");
            BackButtonText = _localization.Observe(table, new TextKey("GameModeWizard.MatchSetup.Back"));
            CancelButtonText = _localization.Observe(table, new TextKey("GameModeWizard.MatchSetup.Cancel"));
            StartButtonText = _localization.Observe(table, new TextKey("GameModeWizard.MatchSetup.Start"));
            OpponentBotText = _localization.Observe(table, new TextKey("GameModeWizard.MatchSetup.Opponent.Bot"));
            OpponentHumanText = _localization.Observe(table, new TextKey("GameModeWizard.MatchSetup.Opponent.Human"));
            OpponentSectionTitle = _localization.Observe(table, new TextKey("GameModeWizard.MatchSetup.Opponent.Title"));
            ModeOptionsTitle = _localization.Observe(table, new TextKey("GameModeWizard.MatchSetup.ModeOptions.Title"));
            BotDifficultyTitle = _localization.Observe(table, new TextKey("GameModeWizard.MatchSetup.BotDifficulty.Title"));
        }

        public override void Initialize()
        {
            base.Initialize();
            EnsureWired();
        }

        public void RequestBack()
        {
            if (!_coordinator.TryPublishIntent(WizardIntent.Back))
                GameLog.Debug("[MatchSetupViewModel] Back intent rejected.");
        }

        public void RequestStart()
        {
            if (!_canStart.Value)
                return;

            if (!_coordinator.TryPublishIntent(WizardIntent.Start))
                GameLog.Debug("[MatchSetupViewModel] Start intent rejected.");
        }

        public void RequestCancel()
        {
            if (!_coordinator.TryPublishIntent(WizardIntent.Cancel))
                GameLog.Debug("[MatchSetupViewModel] Cancel intent rejected.");
        }

        public void SetOpponentType(OpponentType opponentType)
        {
            if (_opponentType.Value == opponentType)
                return;

            _opponentType.Value = opponentType;
        }

        public void SetBotDifficultyId(string? difficultyId)
        {
            var normalized = string.IsNullOrWhiteSpace(difficultyId) ? null : difficultyId;
            if (!string.IsNullOrWhiteSpace(normalized) && !IsDifficultyAvailable(normalized))
                normalized = null;

            if (string.Equals(_selectedDifficultyId.Value, normalized, StringComparison.Ordinal))
                return;

            _selectedDifficultyId.Value = normalized;
        }

        protected override void OnReset()
        {
            Volatile.Write(ref _isWired, 0);
            Volatile.Write(ref _isSyncingFromSession, 0);
            Volatile.Write(ref _isSyncingDifficultyFromSession, 0);
            Volatile.Write(ref _difficultyItemsRebuildScheduled, 0);
            Volatile.Write(ref _difficultyItemsRebuildVersion, 0);

            _session = null;
            _activeModeId = null;

            _modeTitleText.Value = string.Empty;
            _modeIconKey.Value = string.Empty;
            _opponentType.Value = global::Runtime.GameModes.Wizard.OpponentType.Bot;
            _availableDifficulties.Value = _difficultyCatalog.Difficulties;
            _difficultyItems.Value = Array.Empty<DifficultyChipItem>();
            _selectedDifficultyId.Value = null;
            _isBotSettingsVisible.Value = true;
            _difficultyLabels.Clear();
            _difficultyLocalizationSubscriptions?.Dispose();
            _difficultyLocalizationSubscriptions = null;

            _sessionCanStart = false;
            _canStart.Value = false;
            _isBusy.Value = false;
            _lastAppliedVersion = 0;
            _inlineErrorText.Value = null;
            _validationErrorText = null;
            _coordinatorInlineErrorText = null;

            _modeTitleSubscription?.Dispose();
            _modeTitleSubscription = null;

            ReleaseActiveSettings();
        }

        protected override void OnDispose()
        {
            ReleaseActiveSettings();

            _difficultyLocalizationSubscriptions?.Dispose();
            _difficultyLocalizationSubscriptions = null;

            _modeTitleSubscription?.Dispose();
            _modeTitleSubscription = null;

            _modeTitleText.Dispose();
            _modeIconKey.Dispose();
            _activeSettings.Dispose();
            _opponentType.Dispose();
            _availableDifficulties.Dispose();
            _difficultyItems.Dispose();
            _selectedDifficultyId.Dispose();
            _isBotSettingsVisible.Dispose();
            _canStart.Dispose();
            _isBusy.Dispose();
            _inlineErrorText.Dispose();

            base.OnDispose();
        }

        private void EnsureWired()
        {
            if (IsDisposed)
                return;

            if (Interlocked.Exchange(ref _isWired, 1) != 0)
                return;

            AddDisposable(Observable.CombineLatest(
                    _coordinator.IsTransitioning,
                    _coordinator.IsSubmitting,
                    static (isTransitioning, isSubmitting) => isTransitioning || isSubmitting)
                .Subscribe(isBusy => _isBusy.Value = isBusy));

            if (_coordinator.TryGetSession(out var session))
            {
                _session = session;

                AddDisposable(session.Snapshot
                    .DistinctUntilVersionChanged()
                    .Subscribe(new SessionSnapshotObserver(ApplySnapshot)));

                AddDisposable(session.CanStart
                    .Subscribe(OnSessionCanStartChanged));

                AddDisposable(session.ValidationErrors
                    .Subscribe(errors => OnValidationErrorsChanged(errors)));

                AddDisposable(_opponentType
                    .Subscribe(type => OnOpponentTypeChanged(type, session)));

                AddDisposable(_selectedDifficultyId
                    .Subscribe(id => OnSelectedDifficultyChanged(id, session)));
            }

            AddDisposable(_coordinator.CurrentError
                .Subscribe(OnCoordinatorErrorChanged));

            AddDisposable(_opponentType
                .Select(type => type == global::Runtime.GameModes.Wizard.OpponentType.Bot)
                .Subscribe(isBot => _isBotSettingsVisible.Value = isBot));

            AddDisposable(_availableDifficulties
                .Subscribe(OnAvailableDifficultiesChanged));

            UpdateCanStart();
        }

        private void ApplySnapshot(GameModeSessionSnapshot snapshot)
        {
            if (snapshot == null)
                return;

            if (!PlayerLoopHelper.IsMainThread)
            {
                ApplySnapshotOnMainThreadAsync(snapshot).Forget(ex => GameLog.Exception(ex));
                return;
            }

            ApplySnapshotCore(snapshot);
        }

        private void ApplySnapshotCore(GameModeSessionSnapshot snapshot)
        {
            if (snapshot == null)
                return;

            if (snapshot.Version < _lastAppliedVersion)
                return;

            _lastAppliedVersion = snapshot.Version;

            ApplySelectedMode(snapshot.SelectedModeId);
            ApplyOpponentTypeFromSession(snapshot.OpponentType);
            ApplyBotDifficultyFromSession(snapshot.BotDifficultyId);
        }

        private void ApplySelectedMode(string? selectedModeId)
        {
            var normalized = string.IsNullOrWhiteSpace(selectedModeId) ? null : selectedModeId;

            if (string.Equals(_activeModeId, normalized, StringComparison.Ordinal))
                return;

            _activeModeId = normalized;
            UpdateModePresentation(normalized);
        }

        private void UpdateModePresentation(string? modeId)
        {
            ReleaseActiveSettings();

            if (string.IsNullOrWhiteSpace(modeId))
            {
                _modeTitleSubscription?.Dispose();
                _modeTitleSubscription = null;
                _modeTitleText.Value = string.Empty;
                _modeIconKey.Value = string.Empty;
                _activeSettings.Value = null;
                UpdateCanStart();
                return;
            }

            if (!_catalog.TryGetStrategy(modeId, out var strategy) || strategy == null)
            {
                _modeTitleSubscription?.Dispose();
                _modeTitleSubscription = null;
                _modeTitleText.Value = string.Empty;
                _modeIconKey.Value = string.Empty;
                _activeSettings.Value = null;
                UpdateCanStart();
                return;
            }

            _modeIconKey.Value = strategy.Metadata.IconAssetKey;

            _modeTitleSubscription?.Dispose();
            _modeTitleSubscription = _localization
                .Observe(new TextTableId("Mode"), new TextKey(strategy.Metadata.DisplayNameKey))
                .Subscribe(SetModeTitleTextSafe);

            var presentation = strategy.CreatePresentation();
            _activeSettings.Value = presentation;

            _activeSettingsViewModel = presentation.ViewModel;
            if (_activeSettingsViewModel is BaseViewModel baseViewModel)
                baseViewModel.Initialize();

            _activeConfigSubscription = presentation.ViewModel.Config
                .Subscribe(ApplyModeConfig);
            ApplyModeConfig(presentation.ViewModel.Config.CurrentValue);
        }

        private void ApplyOpponentTypeFromSession(OpponentType opponentType)
        {
            if (_opponentType.Value == opponentType)
                return;

            Interlocked.Exchange(ref _isSyncingFromSession, 1);

            try
            {
                _opponentType.Value = opponentType;
            }
            finally
            {
                Interlocked.Exchange(ref _isSyncingFromSession, 0);
            }
        }

        private void ApplyBotDifficultyFromSession(string? difficultyId)
        {
            var normalized = string.IsNullOrWhiteSpace(difficultyId) ? null : difficultyId;
            if (!string.IsNullOrWhiteSpace(normalized) && !IsDifficultyAvailable(normalized))
                normalized = null;

            if (string.Equals(_selectedDifficultyId.Value, normalized, StringComparison.Ordinal))
                return;

            Interlocked.Exchange(ref _isSyncingDifficultyFromSession, 1);

            try
            {
                _selectedDifficultyId.Value = normalized;
            }
            finally
            {
                Interlocked.Exchange(ref _isSyncingDifficultyFromSession, 0);
            }
        }

        private void OnOpponentTypeChanged(OpponentType opponentType, IGameModeSession session)
        {
            if (Volatile.Read(ref _isSyncingFromSession) != 0)
                return;

            OpponentType current;
            try
            {
                current = session.Snapshot.CurrentValue.OpponentType;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            if (current == opponentType)
                return;

            try
            {
                session.Update(s => s.OpponentType == opponentType ? s : s.WithOpponentType(opponentType));
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private void OnSelectedDifficultyChanged(string? difficultyId, IGameModeSession session)
        {
            if (Volatile.Read(ref _isSyncingDifficultyFromSession) != 0)
                return;

            string? current;
            try
            {
                current = session.Snapshot.CurrentValue.BotDifficultyId;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            var normalized = string.IsNullOrWhiteSpace(difficultyId) ? null : difficultyId;
            if (string.Equals(current, normalized, StringComparison.Ordinal))
                return;

            try
            {
                session.Update(s =>
                    string.Equals(s.BotDifficultyId, normalized, StringComparison.Ordinal)
                        ? s
                        : s.WithBotDifficultyId(normalized));
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private void OnAvailableDifficultiesChanged(IReadOnlyList<BotDifficulty> difficulties)
        {
            if (!PlayerLoopHelper.IsMainThread)
            {
                ApplyAvailableDifficultiesOnMainThreadAsync(difficulties).Forget(ex => GameLog.Exception(ex));
                return;
            }

            OnAvailableDifficultiesChangedCore(difficulties);
        }

        private void OnAvailableDifficultiesChangedCore(IReadOnlyList<BotDifficulty> difficulties)
        {
            _difficultyLocalizationSubscriptions?.Dispose();
            _difficultyLocalizationSubscriptions = null;
            _difficultyLabels.Clear();

            if (difficulties == null || difficulties.Count == 0)
            {
                _difficultyItems.Value = Array.Empty<DifficultyChipItem>();
                return;
            }

            var disposables = new CompositeDisposable();
            var table = new TextTableId("GameModeWizard");

            for (var i = 0; i < difficulties.Count; i++)
            {
                var difficulty = difficulties[i];
                if (difficulty == null)
                    throw new InvalidOperationException("Difficulty catalog returned null item.");

                _localization
                    .Observe(table, new TextKey(difficulty.NameKey))
                    .Subscribe(text =>
                    {
                        SetDifficultyLabelSafe(difficulty.Id, text);
                    })
                    .AddTo(disposables);
            }

            _difficultyLocalizationSubscriptions = disposables;
            RequestDifficultyItemsRebuild();
        }

        private void SetDifficultyLabelSafe(string difficultyId, string? text)
        {
            if (PlayerLoopHelper.IsMainThread)
            {
                _difficultyLabels[difficultyId] = text ?? string.Empty;
                RequestDifficultyItemsRebuild();
                return;
            }

            SetDifficultyLabelOnMainThreadAsync(difficultyId, text).Forget(ex => GameLog.Exception(ex));
        }

        private async UniTask SetDifficultyLabelOnMainThreadAsync(string difficultyId, string? text)
        {
            await UniTask.SwitchToMainThread();

            if (IsDisposed)
                return;

            _difficultyLabels[difficultyId] = text ?? string.Empty;
            RequestDifficultyItemsRebuild();
        }

        private void RequestDifficultyItemsRebuild()
        {
            Interlocked.Increment(ref _difficultyItemsRebuildVersion);

            if (Interlocked.Exchange(ref _difficultyItemsRebuildScheduled, 1) != 0)
                return;

            RebuildDifficultyItemsCoalescedAsync().Forget(ex => GameLog.Exception(ex));
        }

        private async UniTask RebuildDifficultyItemsCoalescedAsync()
        {
            await UniTask.SwitchToMainThread();

            if (IsDisposed)
            {
                Interlocked.Exchange(ref _difficultyItemsRebuildScheduled, 0);
                return;
            }

            var observedVersion = Volatile.Read(ref _difficultyItemsRebuildVersion);

            // Coalesce multiple localization emissions into a single rebuild per frame.
            await UniTask.Yield(PlayerLoopTiming.Update);

            if (IsDisposed)
            {
                Interlocked.Exchange(ref _difficultyItemsRebuildScheduled, 0);
                return;
            }

            UpdateDifficultyItems(_availableDifficulties.Value);

            Interlocked.Exchange(ref _difficultyItemsRebuildScheduled, 0);

            if (observedVersion != Volatile.Read(ref _difficultyItemsRebuildVersion))
                RequestDifficultyItemsRebuild();
        }

        private void UpdateDifficultyItems(IReadOnlyList<BotDifficulty> difficulties)
        {
            if (difficulties == null || difficulties.Count == 0)
            {
                _difficultyItems.Value = Array.Empty<DifficultyChipItem>();
                return;
            }

            var items = new DifficultyChipItem[difficulties.Count];
            for (var i = 0; i < difficulties.Count; i++)
            {
                var difficulty = difficulties[i];
                if (difficulty == null)
                    throw new InvalidOperationException("Difficulty catalog returned null item.");

                _difficultyLabels.TryGetValue(difficulty.Id, out var label);
                items[i] = new DifficultyChipItem(difficulty.Id, label ?? string.Empty);
            }

            _difficultyItems.Value = Array.AsReadOnly(items);
        }

        private void SetModeTitleTextSafe(string? text)
        {
            if (PlayerLoopHelper.IsMainThread)
            {
                _modeTitleText.Value = text ?? string.Empty;
                return;
            }

            SetModeTitleTextOnMainThreadAsync(text).Forget(ex => GameLog.Exception(ex));
        }

        private async UniTask SetModeTitleTextOnMainThreadAsync(string? text)
        {
            await UniTask.SwitchToMainThread();

            if (IsDisposed)
                return;

            _modeTitleText.Value = text ?? string.Empty;
        }

        private async UniTask ApplyAvailableDifficultiesOnMainThreadAsync(IReadOnlyList<BotDifficulty> difficulties)
        {
            await UniTask.SwitchToMainThread();

            if (IsDisposed)
                return;

            OnAvailableDifficultiesChangedCore(difficulties);
        }

        private void OnSessionCanStartChanged(bool canStart)
        {
            if (!PlayerLoopHelper.IsMainThread)
            {
                ApplyCanStartOnMainThreadAsync(canStart).Forget(ex => GameLog.Exception(ex));
                return;
            }

            _sessionCanStart = canStart;
            UpdateCanStart();
        }

        private void OnValidationErrorsChanged(IReadOnlyList<ValidationError> errors)
        {
            if (!PlayerLoopHelper.IsMainThread)
            {
                ApplyValidationErrorsOnMainThreadAsync(errors).Forget(ex => GameLog.Exception(ex));
                return;
            }

            _validationErrorText = BuildInlineErrorText(errors);
            UpdateInlineError();
        }

        private void OnCoordinatorErrorChanged(WizardError? error)
        {
            if (!PlayerLoopHelper.IsMainThread)
            {
                ApplyCoordinatorErrorOnMainThreadAsync(error).Forget(ex => GameLog.Exception(ex));
                return;
            }

            if (error == null)
            {
                _coordinatorInlineErrorText = null;
                UpdateInlineError();
                return;
            }

            if (error.DisplayType == ErrorDisplayType.Inline)
            {
                _coordinatorInlineErrorText = ResolveMessageKey(error.MessageKey);
                UpdateInlineError();
                return;
            }

            _coordinatorInlineErrorText = null;
            UpdateInlineError();
        }

        private async UniTask ApplySnapshotOnMainThreadAsync(GameModeSessionSnapshot snapshot)
        {
            await UniTask.SwitchToMainThread();

            if (IsDisposed)
                return;

            if (snapshot.Version < _lastAppliedVersion)
                return;

            ApplySnapshotCore(snapshot);
        }

        private async UniTask ApplyCanStartOnMainThreadAsync(bool canStart)
        {
            await UniTask.SwitchToMainThread();

            if (IsDisposed)
                return;

            _sessionCanStart = canStart;
            UpdateCanStart();
        }

        private async UniTask ApplyValidationErrorsOnMainThreadAsync(IReadOnlyList<ValidationError> errors)
        {
            await UniTask.SwitchToMainThread();

            if (IsDisposed)
                return;

            _validationErrorText = BuildInlineErrorText(errors);
            UpdateInlineError();
        }

        private async UniTask ApplyCoordinatorErrorOnMainThreadAsync(WizardError? error)
        {
            await UniTask.SwitchToMainThread();

            if (IsDisposed)
                return;

            if (error == null)
            {
                _coordinatorInlineErrorText = null;
                UpdateInlineError();
                return;
            }

            if (error.DisplayType == ErrorDisplayType.Inline)
            {
                _coordinatorInlineErrorText = ResolveMessageKey(error.MessageKey);
                UpdateInlineError();
                return;
            }

            _coordinatorInlineErrorText = null;
            UpdateInlineError();
        }

        private string? BuildInlineErrorText(IReadOnlyList<ValidationError> errors)
        {
            if (errors == null || errors.Count == 0)
                return null;

            var bestError = errors[0];
            var bestPriority = GetInlineErrorPriority(bestError.Field);

            for (var i = 1; i < errors.Count; i++)
            {
                var candidate = errors[i];
                var candidatePriority = GetInlineErrorPriority(candidate.Field);
                if (candidatePriority < bestPriority)
                {
                    bestPriority = candidatePriority;
                    bestError = candidate;
                }
            }

            return ResolveMessageKey(bestError.MessageKey);
        }

        private static int GetInlineErrorPriority(string field)
        {
            if (string.IsNullOrWhiteSpace(field))
                return int.MaxValue;

            for (var i = 0; i < InlineErrorPriority.Length; i++)
            {
                if (string.Equals(InlineErrorPriority[i], field, StringComparison.Ordinal))
                    return i;
            }

            return int.MaxValue;
        }

        private string ResolveMessageKey(string messageKey)
        {
            if (string.IsNullOrWhiteSpace(messageKey))
                return string.Empty;

            var dotIndex = messageKey.IndexOf('.', StringComparison.Ordinal);
            if (dotIndex <= 0)
                return messageKey;

            var tableName = messageKey[..dotIndex];
            return _localization.Resolve(new TextTableId(tableName), new TextKey(messageKey));
        }

        private void UpdateInlineError() =>
            _inlineErrorText.Value = _coordinatorInlineErrorText ?? _validationErrorText;

        private void UpdateCanStart() =>
            _canStart.Value = _sessionCanStart;

        private void ApplyModeConfig(IGameModeConfig config)
        {
            if (config == null)
                return;

            var session = _session;
            if (session == null)
                return;

            try
            {
                session.SetModeConfig(config);
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private void ReleaseActiveSettings()
        {
            _activeConfigSubscription?.Dispose();
            _activeConfigSubscription = null;

            if (_activeSettingsViewModel != null)
            {
                try
                {
                    _activeSettingsViewModel.Dispose();
                }
                catch (Exception ex)
                {
                    GameLog.Exception(ex);
                }
                finally
                {
                    _activeSettingsViewModel = null;
                }
            }

            _activeSettings.Value = null;
            UpdateCanStart();
        }

        private bool IsDifficultyAvailable(string difficultyId)
        {
            var difficulties = _availableDifficulties.Value;
            for (var i = 0; i < difficulties.Count; i++)
            {
                if (string.Equals(difficulties[i].Id, difficultyId, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private sealed class SessionSnapshotObserver : Observer<GameModeSessionSnapshot>
        {
            private readonly Action<GameModeSessionSnapshot> _onNext;

            public SessionSnapshotObserver(Action<GameModeSessionSnapshot> onNext) =>
                _onNext = onNext ?? throw new ArgumentNullException(nameof(onNext));

            protected override void OnNextCore(GameModeSessionSnapshot value)
            {
                if (value == null)
                    return;

                _onNext(value);
            }

            protected override void OnErrorResumeCore(Exception error)
            {
                if (error is ObjectDisposedException)
                    return;

                GameLog.Error($"[MatchSetupViewModel] Session snapshot error: {error}");
            }

            protected override void OnCompletedCore(Result result)
            {
            }
        }
    }
}

#nullable restore
