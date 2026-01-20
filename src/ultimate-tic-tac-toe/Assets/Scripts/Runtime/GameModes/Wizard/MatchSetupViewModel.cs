#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.Infrastructure.Logging;
using Runtime.Localization;
using Runtime.UI.Core;

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

        private readonly ReactiveProperty<string> _modeTitleText = new(string.Empty);
        private readonly ReactiveProperty<string> _modeIconKey = new(string.Empty);
        private readonly ReactiveProperty<ModeSettingsPresentation?> _activeSettings = new(null);
        private readonly ReactiveProperty<OpponentType> _opponentType = new(global::Runtime.GameModes.Wizard.OpponentType.Bot);
        private readonly ReactiveProperty<bool> _canStart = new(false);
        private readonly ReactiveProperty<bool> _isBusy = new(false);
        private readonly ReactiveProperty<string?> _inlineErrorText = new(null);

        private IGameModeSession? _session;
        private string? _activeModeId;

        private bool _sessionCanStart;
        private int _lastAppliedVersion;

        private IDisposable? _modeTitleSubscription;
        private string? _validationErrorText;
        private string? _coordinatorInlineErrorText;

        private ISpecificModeSettingsViewModel? _activeSettingsViewModel;
        private IDisposable? _activeConfigSubscription;

        private int _isWired;
        private int _isSyncingFromSession;

        public ReadOnlyReactiveProperty<string> ModeTitleText => _modeTitleText;
        public ReadOnlyReactiveProperty<string> ModeIconKey => _modeIconKey;
        public ReadOnlyReactiveProperty<ModeSettingsPresentation?> ActiveSettings => _activeSettings;
        public ReactiveProperty<OpponentType> OpponentType => _opponentType;
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

        public MatchSetupViewModel(
            IGameModeCatalog catalog,
            IGameModeWizardCoordinator coordinator,
            ILocalizationService localization)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
            _localization = localization ?? throw new ArgumentNullException(nameof(localization));

            var table = new TextTableId("GameModeWizard");
            BackButtonText = _localization.Observe(table, new TextKey("GameModeWizard.MatchSetup.Back"));
            CancelButtonText = _localization.Observe(table, new TextKey("GameModeWizard.MatchSetup.Cancel"));
            StartButtonText = _localization.Observe(table, new TextKey("GameModeWizard.MatchSetup.Start"));
            OpponentBotText = _localization.Observe(table, new TextKey("GameModeWizard.MatchSetup.Opponent.Bot"));
            OpponentHumanText = _localization.Observe(table, new TextKey("GameModeWizard.MatchSetup.Opponent.Human"));
            OpponentSectionTitle = _localization.Observe(table, new TextKey("GameModeWizard.MatchSetup.Opponent.Title"));
            ModeOptionsTitle = _localization.Observe(table, new TextKey("GameModeWizard.MatchSetup.ModeOptions.Title"));
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

        protected override void OnReset()
        {
            Volatile.Write(ref _isWired, 0);
            Volatile.Write(ref _isSyncingFromSession, 0);

            _session = null;
            _activeModeId = null;

            _modeTitleText.Value = string.Empty;
            _modeIconKey.Value = string.Empty;
            _opponentType.Value = global::Runtime.GameModes.Wizard.OpponentType.Bot;

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

            _modeTitleSubscription?.Dispose();
            _modeTitleSubscription = null;

            _modeTitleText.Dispose();
            _modeIconKey.Dispose();
            _activeSettings.Dispose();
            _opponentType.Dispose();
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
            }

            AddDisposable(_coordinator.CurrentError
                .Subscribe(OnCoordinatorErrorChanged));

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
                .Subscribe(text => _modeTitleText.Value = text);

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
