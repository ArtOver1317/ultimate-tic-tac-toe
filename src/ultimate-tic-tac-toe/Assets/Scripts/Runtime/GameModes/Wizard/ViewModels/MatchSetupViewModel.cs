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
    public sealed partial class MatchSetupViewModel : BaseViewModel
    {
        private static readonly string[] _inlineErrorPriority =
        {
            WizardFieldNames.SelectedGameId,
            WizardFieldNames.GameConfig,
            WizardFieldNames.BotDifficultyId,
            WizardFieldNames.Matchmaking,
            WizardFieldNames.GameCatalog,
        };

        private readonly IGameCatalog _catalog;
        private readonly IGameWizardCoordinator _coordinator;
        private readonly ILocalizationService _localization;
        private readonly IBotDifficultyCatalog _difficultyCatalog;
        private readonly IOnlineSessionFlowService _onlineSessionFlow;

        private readonly ReactiveProperty<string> _modeTitleText = new(string.Empty);
        private readonly ReactiveProperty<string> _modeIconKey = new(string.Empty);
        private readonly ReactiveProperty<GameSettingsPresentation?> _activeSettings = new(null);
        private readonly ReactiveProperty<OpponentType> _opponentType = new(global::Runtime.GameModes.Wizard.OpponentType.Bot);
        private readonly ReactiveProperty<HumanOpponentKind> _humanOpponentKind = new(global::Runtime.GameModes.Wizard.HumanOpponentKind.Local);
        private readonly ReactiveProperty<IReadOnlyList<BotDifficulty>> _availableDifficulties;
        private readonly ReactiveProperty<IReadOnlyList<DifficultyChipItem>> _difficultyItems = new(Array.Empty<DifficultyChipItem>());
        private readonly ReactiveProperty<string?> _selectedDifficultyId = new(null);
        private readonly ReactiveProperty<bool> _isBotSettingsVisible = new(true);
        private readonly ReactiveProperty<bool> _isHumanSettingsVisible = new(false);
        private readonly ReactiveProperty<bool> _isPlayerIdInputVisible = new(false);
        private readonly ReactiveProperty<string> _targetPlayerId = new(string.Empty);
        private readonly ReactiveProperty<string?> _playerIdErrorText = new(null);
        private readonly ReactiveProperty<bool> _canStart = new(false);
        private readonly ReactiveProperty<bool> _isBusy = new(false);
        private readonly ReactiveProperty<string?> _inlineErrorText = new(null);
        private readonly ReactiveProperty<bool> _onlinePanelVisible = new(false);
        private readonly ReactiveProperty<string> _visibleSessionId = new(string.Empty);
        private readonly ReactiveProperty<string?> _onlineStatusText = new(null);
        private readonly ReactiveProperty<string?> _onlineCountdownText = new(null);
        private readonly ReactiveProperty<bool> _canCopySessionId = new(false);
        private readonly ReactiveProperty<bool> _canBecomeHost = new(false);
        private readonly ReactiveProperty<bool> _isModeOptionsEnabled = new(true);

        private IGameSession? _session;
        private string? _activeModeId;

        private bool _sessionCanStart;
        private int _lastAppliedVersion;
        private IGameConfig? _lastAppliedModeConfig;

        private IDisposable? _modeTitleSubscription;
        private CompositeDisposable? _difficultyLocalizationSubscriptions;
        private readonly Dictionary<string, string> _difficultyLabels = new(StringComparer.Ordinal);
        private string? _validationErrorText;
        private string? _coordinatorInlineErrorText;

        private int _difficultyItemsRebuildScheduled;
        private int _difficultyItemsRebuildVersion;

#if UNITY_INCLUDE_TESTS || UNITY_EDITOR
        private bool _disablePlayerLoopForTests;
#endif

        private IGameSettingsViewModel? _activeSettingsViewModel;
        private IDisposable? _activeConfigSubscription;

        private int _isWired;
        
        // Protects against feedback loop: session -> subVM -> session
        private int _isSyncingModeConfigFromSession;
        private int _disposedExceptionLogged;

        public ReadOnlyReactiveProperty<string> ModeTitleText => _modeTitleText;
        public ReadOnlyReactiveProperty<string> ModeIconKey => _modeIconKey;
        public ReadOnlyReactiveProperty<GameSettingsPresentation?> ActiveSettings => _activeSettings;
        public ReadOnlyReactiveProperty<OpponentType> OpponentType => _opponentType;
        public ReadOnlyReactiveProperty<HumanOpponentKind> HumanOpponentKind => _humanOpponentKind;
        public ReadOnlyReactiveProperty<IReadOnlyList<BotDifficulty>> AvailableDifficulties => _availableDifficulties;
        public ReadOnlyReactiveProperty<IReadOnlyList<DifficultyChipItem>> DifficultyItems => _difficultyItems;
        public ReadOnlyReactiveProperty<string?> SelectedDifficultyId => _selectedDifficultyId;
        public ReadOnlyReactiveProperty<bool> IsBotSettingsVisible => _isBotSettingsVisible;
        public ReadOnlyReactiveProperty<bool> IsHumanSettingsVisible => _isHumanSettingsVisible;
        public ReadOnlyReactiveProperty<bool> IsPlayerIdInputVisible => _isPlayerIdInputVisible;
        public ReadOnlyReactiveProperty<string> TargetPlayerId => _targetPlayerId;
        public ReadOnlyReactiveProperty<string?> PlayerIdErrorText => _playerIdErrorText;
        public ReadOnlyReactiveProperty<bool> CanStart => _canStart;
        public ReadOnlyReactiveProperty<bool> IsBusy => _isBusy;
        public ReadOnlyReactiveProperty<bool> OnlinePanelVisible => _onlinePanelVisible;
        public ReadOnlyReactiveProperty<string> VisibleSessionId => _visibleSessionId;
        public ReadOnlyReactiveProperty<string?> OnlineStatusText => _onlineStatusText;
        public ReadOnlyReactiveProperty<string?> OnlineCountdownText => _onlineCountdownText;
        public ReadOnlyReactiveProperty<bool> CanCopySessionId => _canCopySessionId;
        public ReadOnlyReactiveProperty<bool> CanBecomeHost => _canBecomeHost;
        public ReadOnlyReactiveProperty<bool> IsModeOptionsEnabled => _isModeOptionsEnabled;
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
        public Observable<string> HumanSettingsTitle { get; }
        public Observable<string> HumanLocalText { get; }
        public Observable<string> HumanDirectInviteText { get; }
        public Observable<string> HumanMatchmakingText { get; }
        public Observable<string> PlayerIdLabelText { get; }
        public Observable<string> SessionIdLabelText { get; }
        public Observable<string> CopySessionIdButtonText { get; }
        public Observable<string> BecomeHostButtonText { get; }

        public MatchSetupViewModel(
            IGameCatalog catalog,
            IGameWizardCoordinator coordinator,
            ILocalizationService localization,
            IBotDifficultyCatalog difficultyCatalog,
            IOnlineSessionFlowService? onlineSessionFlow = null)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
            _localization = localization ?? throw new ArgumentNullException(nameof(localization));
            _difficultyCatalog = difficultyCatalog ?? throw new ArgumentNullException(nameof(difficultyCatalog));
            _onlineSessionFlow = onlineSessionFlow ?? NoOpOnlineSessionFlowService.Instance;

            _availableDifficulties = new ReactiveProperty<IReadOnlyList<BotDifficulty>>(
                _difficultyCatalog.Difficulties ?? throw new ArgumentException("Difficulty catalog returned null list.", nameof(difficultyCatalog)));

            var table = new TextTableId("GameWizard");
            BackButtonText = _localization.Observe(table, new TextKey("GameWizard.MatchSetup.Back"));
            CancelButtonText = _localization.Observe(table, new TextKey("GameWizard.MatchSetup.Cancel"));
            StartButtonText = _localization.Observe(table, new TextKey("GameWizard.MatchSetup.Start"));
            OpponentBotText = _localization.Observe(table, new TextKey("GameWizard.MatchSetup.Opponent.Bot"));
            OpponentHumanText = _localization.Observe(table, new TextKey("GameWizard.MatchSetup.Opponent.Human"));
            OpponentSectionTitle = _localization.Observe(table, new TextKey("GameWizard.MatchSetup.Opponent.Title"));
            ModeOptionsTitle = _localization.Observe(table, new TextKey("GameWizard.MatchSetup.ModeOptions.Title"));
            BotDifficultyTitle = _localization.Observe(table, new TextKey("GameWizard.MatchSetup.BotDifficulty.Title"));
            HumanSettingsTitle = _localization.Observe(table, new TextKey("GameWizard.MatchSetup.HumanSettings.Title"));
            HumanLocalText = _localization.Observe(table, new TextKey("GameWizard.MatchSetup.HumanSettings.Local"));
            HumanDirectInviteText = _localization.Observe(table, new TextKey("GameWizard.MatchSetup.HumanSettings.DirectInvite"));
            HumanMatchmakingText = _localization.Observe(table, new TextKey("GameWizard.MatchSetup.HumanSettings.Matchmaking"));
            PlayerIdLabelText = _localization.Observe(table, new TextKey("GameWizard.MatchSetup.PlayerId.Label"));
            SessionIdLabelText = _localization.Observe(table, new TextKey("GameWizard.MatchSetup.SessionId.Label"));
            CopySessionIdButtonText = _localization.Observe(table, new TextKey("GameWizard.MatchSetup.SessionId.Copy"));
            BecomeHostButtonText = _localization.Observe(table, new TextKey("GameWizard.MatchSetup.Host.Become"));
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
            if (TryRequestOnlineSoftCancel())
                return;

            if (!_coordinator.TryPublishIntent(WizardIntent.Cancel))
                GameLog.Debug("[MatchSetupViewModel] Cancel intent rejected.");
        }

        public void AcknowledgeError() => _coordinator.ClearCurrentError();

        public void RequestCopySessionId() => RequestCopySessionIdAsync().Forget();

        public void RequestBecomeHost() => RequestBecomeHostAsync().Forget();

        public void SetOpponentType(OpponentType opponentType)
        {
            if (IsDisposed)
                return;

            if (_opponentType.CurrentValue == opponentType)
                return;

            var session = _session;
            
            if (session == null)
            {
                GameLog.Warning("[MatchSetupViewModel] SetOpponentType ignored: session not available.");
                return;
            }

            try
            {
                session.Update(s => s.OpponentType == opponentType ? s : s.WithOpponentType(opponentType));
            }
            catch (ObjectDisposedException)
            {
                LogDisposedOnce("SetOpponentType");
            }
        }

        public void SetHumanOpponentKind(HumanOpponentKind kind)
        {
            if (IsDisposed)
                return;

            if (_humanOpponentKind.CurrentValue == kind)
                return;

            var session = _session;
            
            if (session == null)
            {
                GameLog.Warning("[MatchSetupViewModel] SetHumanOpponentKind ignored: session not available.");
                return;
            }

            try
            {
                session.Update(s => s.HumanOpponentKind == kind ? s : s.WithHumanOpponentKind(kind));
            }
            catch (ObjectDisposedException)
            {
                LogDisposedOnce("SetHumanOpponentKind");
            }
        }

        public void SetBotDifficultyId(string? difficultyId)
        {
            if (IsDisposed)
                return;

            var normalized = string.IsNullOrWhiteSpace(difficultyId) ? null : difficultyId;
            
            if (!string.IsNullOrWhiteSpace(normalized) && !IsDifficultyAvailable(normalized))
                normalized = null;

            if (string.Equals(_selectedDifficultyId.CurrentValue, normalized, StringComparison.Ordinal))
                return;

            var session = _session;
            
            if (session == null)
            {
                GameLog.Warning("[MatchSetupViewModel] SetBotDifficultyId ignored: session not available.");
                return;
            }

            try
            {
                session.Update(s =>
                    string.Equals(s.BotDifficultyId, normalized, StringComparison.Ordinal)
                        ? s
                        : s.WithBotDifficultyId(normalized));
            }
            catch (ObjectDisposedException)
            {
                LogDisposedOnce("SetBotDifficultyId");
            }
        }

#if UNITY_INCLUDE_TESTS || UNITY_EDITOR
        internal void SetDifficultyItemsForTests(IReadOnlyList<DifficultyChipItem>? items) =>
            _difficultyItems.Value = items ?? Array.Empty<DifficultyChipItem>();

        internal void DisablePlayerLoopForTests() => _disablePlayerLoopForTests = true;
#endif

        protected override void OnReset()
        {
            Volatile.Write(ref _isWired, 0);
            Volatile.Write(ref _difficultyItemsRebuildScheduled, 0);
            Volatile.Write(ref _difficultyItemsRebuildVersion, 0);

            _session = null;
            _activeModeId = null;

            _modeTitleText.Value = string.Empty;
            _modeIconKey.Value = string.Empty;
            _opponentType.Value = global::Runtime.GameModes.Wizard.OpponentType.Bot;
            _humanOpponentKind.Value = global::Runtime.GameModes.Wizard.HumanOpponentKind.Local;
            _availableDifficulties.Value = _difficultyCatalog.Difficulties;
            _difficultyItems.Value = Array.Empty<DifficultyChipItem>();
            _selectedDifficultyId.Value = null;
            _isBotSettingsVisible.Value = true;
            _isHumanSettingsVisible.Value = false;
            _isPlayerIdInputVisible.Value = false;
            _targetPlayerId.Value = string.Empty;
            _playerIdErrorText.Value = null;
            _difficultyLabels.Clear();
            _difficultyLocalizationSubscriptions?.Dispose();
            _difficultyLocalizationSubscriptions = null;

            _sessionCanStart = false;
            _canStart.Value = false;
            _isBusy.Value = false;
            _onlinePanelVisible.Value = false;
            _visibleSessionId.Value = string.Empty;
            _onlineStatusText.Value = null;
            _onlineCountdownText.Value = null;
            _canCopySessionId.Value = false;
            _canBecomeHost.Value = false;
            _isModeOptionsEnabled.Value = true;
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
            _humanOpponentKind.Dispose();
            _availableDifficulties.Dispose();
            _difficultyItems.Dispose();
            _selectedDifficultyId.Dispose();
            _isBotSettingsVisible.Dispose();
            _isHumanSettingsVisible.Dispose();
            _isPlayerIdInputVisible.Dispose();
            _targetPlayerId.Dispose();
            _playerIdErrorText.Dispose();
            _canStart.Dispose();
            _isBusy.Dispose();
            _onlinePanelVisible.Dispose();
            _visibleSessionId.Dispose();
            _onlineStatusText.Dispose();
            _onlineCountdownText.Dispose();
            _canCopySessionId.Dispose();
            _canBecomeHost.Dispose();
            _isModeOptionsEnabled.Dispose();
            _inlineErrorText.Dispose();

            base.OnDispose();
        }

        private void EnsureWired()
        {
            if (IsDisposed)
                return;

            if (Interlocked.Exchange(ref _isWired, 1) != 0)
                return;

            WireBusyState();
            WireSessionSubscriptions();
            WireUISubscriptions();
            WireOnlineSubscriptions();

            AddDisposable(_availableDifficulties
                .Subscribe(OnAvailableDifficultiesChanged));

            UpdateCanStart();
        }

        private void WireBusyState()
        {
            AddDisposable(Observable.CombineLatest(
                    _coordinator.IsTransitioning,
                    _coordinator.IsSubmitting,
                    static (isTransitioning, isSubmitting) => isTransitioning || isSubmitting)
                .Subscribe(isBusy => _isBusy.Value = isBusy));
        }

        private void WireSessionSubscriptions()
        {
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
            }
            else
            {
                _validationErrorText = ResolveMessageKey("Errors.GameWizard.SessionNotReady");
                UpdateInlineError();
            }
        }

        private void WireUISubscriptions()
        {
            AddDisposable(_coordinator.CurrentError
                .Subscribe(OnCoordinatorErrorChanged));

            AddDisposable(_opponentType
                .Select(type => type == global::Runtime.GameModes.Wizard.OpponentType.Bot)
                .Subscribe(isBot => _isBotSettingsVisible.Value = isBot));

            AddDisposable(_opponentType
                .Select(type => type == global::Runtime.GameModes.Wizard.OpponentType.Human)
                .Subscribe(isHuman => _isHumanSettingsVisible.Value = isHuman));

            AddDisposable(Observable.CombineLatest(
                    _opponentType,
                    _humanOpponentKind,
                    static (opponentType, humanKind) =>
                        opponentType == global::Runtime.GameModes.Wizard.OpponentType.Human &&
                        humanKind == global::Runtime.GameModes.Wizard.HumanOpponentKind.DirectInvite)
                .Subscribe(isVisible => _isPlayerIdInputVisible.Value = isVisible));
        }

    }
}
