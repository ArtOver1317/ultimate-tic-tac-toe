#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using R3;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.Coordinator;
using Runtime.GameModes.Wizard.Modes;
using Runtime.GameModes.Wizard.Online;
using Runtime.GameModes.Wizard.Session;
using Runtime.Infrastructure.Logging;
using Runtime.Localization;
using Runtime.Localization.Contracts;
using Runtime.UI.Components;
using Runtime.UI.Core;
using WizardHumanOpponentKind = Runtime.GameModes.Wizard.Session.HumanOpponentKind;
using WizardOpponentType = Runtime.GameModes.Wizard.Session.OpponentType;

namespace Runtime.GameModes.Wizard.ViewModels.MatchSetup
{
    /// <summary>
    /// View-model for the match setup step of the game mode wizard.
    /// Coordinates specialized MatchSetup collaborators and exposes UI-facing state.
    /// </summary>
    public sealed class MatchSetupViewModel : BaseViewModel
    {
        private readonly IGameWizardCoordinator _coordinator;
        private readonly MatchSetupTexts _texts;
        private readonly MatchSetupModePresentation _modePresentation;
        private readonly MatchSetupDifficultySelection _difficultySelection;
        private readonly MatchSetupInviteSessionField _inviteSessionField;
        private readonly MatchSetupOnlinePanel _onlinePanel;
        private readonly MatchSetupSessionBinding _sessionBinding;

        private readonly ReactiveProperty<OpponentType> _opponentType = new(WizardOpponentType.Bot);
        private readonly ReactiveProperty<HumanOpponentKind> _humanOpponentKind = new(WizardHumanOpponentKind.Local);
        private readonly ReactiveProperty<bool> _isHumanSettingsVisible = new(false);
        private readonly ReactiveProperty<bool> _canStart = new(false);
        private readonly ReactiveProperty<bool> _isBusy = new(false);
        private readonly ReactiveProperty<string?> _inlineErrorText = new(null);

#if UNITY_INCLUDE_TESTS || UNITY_EDITOR
        private bool _disablePlayerLoopForTests;
#endif

        private int _isWired;
        private int _disposedExceptionLogged;

        public ReadOnlyReactiveProperty<string> ModeTitleText => _modePresentation.ModeTitleText;
        public ReadOnlyReactiveProperty<string> ModeIconKey => _modePresentation.ModeIconKey;
        public ReadOnlyReactiveProperty<GameSettingsPresentation?> ActiveSettings => _modePresentation.ActiveSettings;
        public ReadOnlyReactiveProperty<OpponentType> OpponentType => _opponentType;
        public ReadOnlyReactiveProperty<HumanOpponentKind> HumanOpponentKind => _humanOpponentKind;
        public ReadOnlyReactiveProperty<IReadOnlyList<BotDifficulty>> AvailableDifficulties => _modePresentation.AvailableDifficulties;
        public ReadOnlyReactiveProperty<IReadOnlyList<DifficultyChipItem>> DifficultyItems => _difficultySelection.DifficultyItems;
        public ReadOnlyReactiveProperty<string?> SelectedDifficultyId => _difficultySelection.SelectedDifficultyId;
        public ReadOnlyReactiveProperty<bool> IsBotSettingsVisible => _difficultySelection.IsBotSettingsVisible;
        public ReadOnlyReactiveProperty<bool> IsHumanSettingsVisible => _isHumanSettingsVisible;
        public ReadOnlyReactiveProperty<bool> IsLocalHumanSupported => _modePresentation.IsLocalHumanSupported;
        public ReadOnlyReactiveProperty<bool> IsPlayerIdInputVisible => _inviteSessionField.IsPlayerIdInputVisible;
        public ReadOnlyReactiveProperty<string> TargetPlayerId => _inviteSessionField.TargetPlayerId;
        public ReadOnlyReactiveProperty<string?> PlayerIdErrorText => _inviteSessionField.PlayerIdErrorText;
        public ReadOnlyReactiveProperty<bool> CanStart => _canStart;
        public ReadOnlyReactiveProperty<bool> IsBusy => _isBusy;
        public ReadOnlyReactiveProperty<bool> OnlinePanelVisible => _onlinePanel.OnlinePanelVisible;
        public ReadOnlyReactiveProperty<string> VisibleSessionId => _onlinePanel.VisibleSessionId;
        public ReadOnlyReactiveProperty<string?> OnlineStatusText => _onlinePanel.OnlineStatusText;
        public ReadOnlyReactiveProperty<string?> OnlineCountdownText => _onlinePanel.OnlineCountdownText;
        public ReadOnlyReactiveProperty<bool> CanCopySessionId => _onlinePanel.CanCopySessionId;
        public ReadOnlyReactiveProperty<bool> CanBecomeHost => _onlinePanel.CanBecomeHost;
        public ReadOnlyReactiveProperty<bool> IsModeOptionsEnabled => _onlinePanel.IsModeOptionsEnabled;
        public ReadOnlyReactiveProperty<WizardError?> Error => _coordinator.CurrentError;
        public ReadOnlyReactiveProperty<string?> InlineErrorText => _inlineErrorText;
        public MoveTimerSettingsViewModel MoveTimerSettings { get; }

        public Observable<string> BackButtonText => _texts.BackButtonText;
        public Observable<string> CancelButtonText => _texts.CancelButtonText;
        public Observable<string> StartButtonText => _texts.StartButtonText;
        public Observable<string> OpponentBotText => _texts.OpponentBotText;
        public Observable<string> OpponentHumanText => _texts.OpponentHumanText;
        public Observable<string> OpponentSectionTitle => _texts.OpponentSectionTitle;
        public Observable<string> ModeOptionsTitle => _texts.ModeOptionsTitle;
        public Observable<string> BotDifficultyTitle => _texts.BotDifficultyTitle;
        public Observable<string> HumanSettingsTitle => _texts.HumanSettingsTitle;
        public Observable<string> HumanLocalText => _texts.HumanLocalText;
        public Observable<string> HumanDirectInviteText => _texts.HumanDirectInviteText;
        public Observable<string> HumanMatchmakingText => _texts.HumanMatchmakingText;
        public Observable<string> PlayerIdLabelText => _texts.PlayerIdLabelText;
        public Observable<string> SessionIdLabelText => _texts.SessionIdLabelText;
        public Observable<string> CopySessionIdButtonText => _texts.CopySessionIdButtonText;
        public Observable<string> BecomeHostButtonText => _texts.BecomeHostButtonText;

        public MatchSetupViewModel(
            IGameCatalog catalog,
            IGameWizardCoordinator coordinator,
            ILocalizationService localization,
            IBotDifficultyCatalog difficultyCatalog,
            IOnlineSessionFlowService? onlineSessionFlow)
            : this(
                catalog,
                coordinator,
                localization,
                difficultyCatalog,
                moveTimerPresetsConfig: null,
                onlineSessionFlow: onlineSessionFlow) { }

        public MatchSetupViewModel(
            IGameCatalog catalog,
            IGameWizardCoordinator coordinator,
            ILocalizationService localization,
            IBotDifficultyCatalog difficultyCatalog,
            MoveTimerPresetsConfig? moveTimerPresetsConfig = null,
            IOnlineSessionFlowService? onlineSessionFlow = null)
        {
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));

            if (difficultyCatalog == null)
                throw new ArgumentNullException(nameof(difficultyCatalog));

            _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
            _texts = new MatchSetupTexts(localization ?? throw new ArgumentNullException(nameof(localization)));
            
            MoveTimerSettings = new MoveTimerSettingsViewModel(
                moveTimerPresetsConfig != null ? moveTimerPresetsConfig : MoveTimerPresetsConfig.CreateRuntimeDefault(),
                localization);

            _modePresentation = new MatchSetupModePresentation(
                catalog,
                localization,
                difficultyCatalog,
                ApplyModeConfigToSession,
                RefreshCanStartFromModePresentation,
                IsDisposedForControllers,
                IsPlayerLoopDisabledForControllers);
           
            _difficultySelection = new MatchSetupDifficultySelection(
                localization,
                _modePresentation.AvailableDifficulties,
                OpponentType,
                GetCurrentSession,
                _modePresentation.GetActiveModeId,
                LogDisposedOnce,
                IsDisposedForControllers,
                IsPlayerLoopDisabledForControllers);
            
            _inviteSessionField = new MatchSetupInviteSessionField(
                OpponentType,
                HumanOpponentKind,
                GetCurrentSession,
                _texts.ResolveMessageKey,
                LogDisposedOnce);
            
            _onlinePanel = new MatchSetupOnlinePanel(
                onlineSessionFlow ?? NoOpOnlineSessionFlowService.Instance,
                OpponentType,
                HumanOpponentKind,
                _texts.ResolveMessageKey,
                IsDisposedForControllers,
                _inviteSessionField.SetTargetPlayerId);
            
            _sessionBinding = new MatchSetupSessionBinding(
                coordinator,
                _opponentType,
                _humanOpponentKind,
                _canStart,
                _isBusy,
                _inlineErrorText,
                _modePresentation,
                _difficultySelection,
                _inviteSessionField,
                MoveTimerSettings,
                AddOwnedDisposable,
                _texts.ResolveMessageKey,
                LogDisposedOnce,
                IsDisposedForControllers,
                IsPlayerLoopDisabledForControllers);
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
            if (_onlinePanel.TryRequestSoftCancel())
                return;

            if (!_coordinator.TryPublishIntent(WizardIntent.Cancel))
                GameLog.Debug("[MatchSetupViewModel] Cancel intent rejected.");
        }

        public void AcknowledgeError() => _coordinator.ClearCurrentError();

        public void RequestCopySessionId() => _onlinePanel.RequestCopySessionId();

        public void RequestBecomeHost() => _onlinePanel.RequestBecomeHost();

        public void SetOpponentType(OpponentType opponentType)
        {
            if (IsDisposed || _opponentType.CurrentValue == opponentType)
                return;

            TryUpdateCurrentSession(
                "SetOpponentType",
                snapshot => snapshot.OpponentType == opponentType
                    ? snapshot
                    : snapshot.WithOpponentType(opponentType));
        }

        public void SetHumanOpponentKind(HumanOpponentKind kind)
        {
            if (IsDisposed || _humanOpponentKind.CurrentValue == kind)
                return;

            TryUpdateCurrentSession(
                "SetHumanOpponentKind",
                snapshot => snapshot.HumanOpponentKind == kind
                    ? snapshot
                    : snapshot.WithHumanOpponentKind(kind));
        }

        public void SetBotDifficultyId(string? difficultyId)
        {
            if (IsDisposed)
                return;

            _difficultySelection.SetBotDifficultyId(difficultyId);
        }

        public void SetTargetPlayerId(string? playerId)
        {
            if (IsDisposed)
                return;

            _inviteSessionField.SetTargetPlayerId(playerId);
        }

#if UNITY_INCLUDE_TESTS || UNITY_EDITOR
        internal void SetDifficultyItemsForTests(IReadOnlyList<DifficultyChipItem>? items) =>
            _difficultySelection.SetDifficultyItemsForTests(items);

        internal void DisablePlayerLoopForTests() => _disablePlayerLoopForTests = true;
#endif

        protected override void OnReset()
        {
            Volatile.Write(ref _isWired, 0);
            Interlocked.Exchange(ref _disposedExceptionLogged, 0);

            _opponentType.Value = WizardOpponentType.Bot;
            _humanOpponentKind.Value = WizardHumanOpponentKind.Local;
            _isHumanSettingsVisible.Value = false;

            _sessionBinding.Reset();
            _modePresentation.Reset();
            _difficultySelection.Reset();
            _inviteSessionField.Reset();
            _onlinePanel.Reset();
            MoveTimerSettings.TryApplyConfig(0);
        }

        protected override void OnDispose()
        {
            _modePresentation.Dispose();
            _difficultySelection.Dispose();
            _inviteSessionField.Dispose();
            _onlinePanel.Dispose();
            _opponentType.Dispose();
            _humanOpponentKind.Dispose();
            _isHumanSettingsVisible.Dispose();
            _canStart.Dispose();
            _isBusy.Dispose();
            _inlineErrorText.Dispose();
            MoveTimerSettings.Dispose();

            base.OnDispose();
        }

        private void EnsureWired()
        {
            if (!TryBeginWiring())
                return;

            _sessionBinding.Wire();
            _difficultySelection.Wire(AddOwnedDisposable);
            _inviteSessionField.Wire(AddOwnedDisposable);
            _onlinePanel.Wire(AddOwnedDisposable);
            
            AddDisposable(_opponentType
                .Select(type => type == WizardOpponentType.Human)
                .Subscribe(isHuman => _isHumanSettingsVisible.Value = isHuman));
        }

        private bool TryBeginWiring()
        {
            if (IsDisposed)
                return false;

            return Interlocked.Exchange(ref _isWired, 1) == 0;
        }

        private void ApplyModeConfigToSession(IGameConfig? config)
        {
            if (config == null)
                return;

            var session = TryGetCurrentSession("ApplyModeConfig");

            if (session == null)
                return;

            try
            {
                session.SetModeConfig(config);
            }
            catch (ObjectDisposedException)
            {
                LogDisposedOnce("ApplyModeConfig");
            }
        }

        private void TryUpdateCurrentSession(
            string context,
            Func<GameSessionSnapshot, GameSessionSnapshot> update)
        {
            var session = TryGetCurrentSession(context);

            if (session == null)
                return;

            try
            {
                session.Update(update);
            }
            catch (ObjectDisposedException)
            {
                LogDisposedOnce(context);
            }
        }

        private IGameSession? TryGetCurrentSession(string context)
        {
            var session = _sessionBinding.CurrentSession;

            if (session != null)
                return session;

            GameLog.Warning($"[MatchSetupViewModel] {context} ignored: session not available.");
            return null;
        }

        private IGameSession? GetCurrentSession() => _sessionBinding.CurrentSession;

        private void AddOwnedDisposable(IDisposable disposable) => AddDisposable(disposable);

        private void RefreshCanStartFromModePresentation() => _sessionBinding?.RefreshCanStart();

        private bool IsDisposedForControllers() => IsDisposed;

        private bool IsPlayerLoopDisabledForControllers()
        {
#if UNITY_INCLUDE_TESTS || UNITY_EDITOR
            return _disablePlayerLoopForTests;
#else
            return false;
#endif
        }

        private void LogDisposedOnce(string context)
        {
            if (Interlocked.Exchange(ref _disposedExceptionLogged, 1) != 0)
                return;

            GameLog.Debug($"[MatchSetupViewModel] Ignored ObjectDisposedException in {context}.");
        }
    }
}