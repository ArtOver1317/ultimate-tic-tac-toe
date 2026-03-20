using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.GameModes.Wizard;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.Coordinator;
using Runtime.GameModes.Wizard.Online;
using Runtime.Infrastructure.GameStateMachine;
using Runtime.Infrastructure.GameStateMachine.States;
using Runtime.Infrastructure.Logging;
using StripLog;

namespace Runtime.UI.MainMenu
{
    internal sealed class MainMenuMatchStartFlow : IDisposable
    {
        private readonly IGameStateMachine _stateMachine;
        private readonly IGameWizardCoordinator _wizardCoordinator;
        private readonly IOnlineSessionLauncher _onlineSessionLauncher;
        private readonly IOnlineSessionFlowService _onlineSessionFlow;
        private readonly MainMenuOverlayNavigator _overlayNavigator;

        private CompositeDisposable _subscriptions = new();

        // Transition token: intentionally not linked to lifecycle.
        // MainMenuCoordinator.Dispose() is called during normal scene exit, so linking would cancel valid transitions.
        private CancellationTokenSource _launchCts;
        private MainMenuViewModel _viewModel;
        private CancellationToken _lifecycleToken;
        private int _startInProgress;
        private int _wizardStartInProgress;
        private OnlineFlowState _lastOnlineFlowState = OnlineFlowState.Idle;
        private bool _hasOnlineFlowState;

        public MainMenuMatchStartFlow(
            IGameStateMachine stateMachine,
            IGameWizardCoordinator wizardCoordinator,
            IOnlineSessionLauncher onlineSessionLauncher,
            IOnlineSessionFlowService onlineSessionFlow,
            MainMenuOverlayNavigator overlayNavigator)
        {
            _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
            _wizardCoordinator = wizardCoordinator ?? throw new ArgumentNullException(nameof(wizardCoordinator));
            _onlineSessionLauncher = onlineSessionLauncher ?? throw new ArgumentNullException(nameof(onlineSessionLauncher));
            _onlineSessionFlow = onlineSessionFlow ?? throw new ArgumentNullException(nameof(onlineSessionFlow));
            _overlayNavigator = overlayNavigator ?? throw new ArgumentNullException(nameof(overlayNavigator));
        }

        public void Initialize(MainMenuViewModel viewModel, CancellationToken lifecycleToken)
        {
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            _lifecycleToken = lifecycleToken;

            _subscriptions.Dispose();
            _subscriptions = new CompositeDisposable();

            _wizardCoordinator.GameLaunchRequested
                .Subscribe(config => OnLaunchRequestedAsync(config, _lifecycleToken).Forget(MainMenuAsyncExceptionHandler.HandleFireAndForgetException))
                .AddTo(_subscriptions);

            _wizardCoordinator.WizardAborted
                .Subscribe(HandleWizardAborted)
                .AddTo(_subscriptions);

            _onlineSessionFlow.Snapshot
                .Subscribe(HandleOnlineFlowSnapshot)
                .AddTo(_subscriptions);
        }

        public void Reset()
        {
            _subscriptions.Dispose();
            _subscriptions = new CompositeDisposable();
            _viewModel = null;
            _lifecycleToken = default;
            _startInProgress = 0;
            _wizardStartInProgress = 0;
            _hasOnlineFlowState = false;
            _lastOnlineFlowState = OnlineFlowState.Idle;
            _launchCts?.Cancel();
            _launchCts?.Dispose();
            _launchCts = null;
        }

        public void Dispose()
        {
            if (_startInProgress == 0)
            {
                _launchCts?.Cancel();
                _launchCts?.Dispose();
                _launchCts = null;
            }

            _subscriptions.Dispose();
        }

        public async UniTask OnStartGameAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Log.Debug(LogTags.UI, "[MainMenuCoordinator] Starting game...");

            if (Interlocked.Exchange(ref _wizardStartInProgress, 1) != 0)
                return;

            var viewModel = RequireViewModel();
            _overlayNavigator.CloseTransientOverlays();
            viewModel.SetInteractable(false);

            try
            {
                await _wizardCoordinator.StartWizardAsync(cancellationToken);
                _overlayNavigator.HideMainMenu();
            }
            catch (OperationCanceledException)
            {
                _overlayNavigator.ShowMainMenu(viewModel);
                throw;
            }
            catch (Exception exception)
            {
                _overlayNavigator.ShowMainMenu(viewModel);
                Log.Exception(exception, LogTags.UI);
            }
            finally
            {
                Interlocked.Exchange(ref _wizardStartInProgress, 0);
            }
        }

        private async UniTask OnLaunchRequestedAsync(GameLaunchConfig config, CancellationToken cancellationToken)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            if (Interlocked.Exchange(ref _startInProgress, 1) != 0)
                return;

            var viewModel = RequireViewModel();

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                viewModel.SetInteractable(false);

                _launchCts?.Cancel();
                _launchCts?.Dispose();
                _launchCts = new CancellationTokenSource();
                var launchToken = _launchCts.Token;

                var preparation = await _onlineSessionLauncher.PrepareForLaunchAsync(config, launchToken);
                
                if (!preparation.IsSuccess)
                {
                    viewModel.SetInteractable(true);

                    if (_wizardCoordinator.IsActive)
                    {
                        _wizardCoordinator.CompleteStartAttempt(
                            succeeded: false,
                            error: preparation.Error ?? CreateOnlinePreparationError());
                    }

                    return;
                }

                await _stateMachine.EnterAsync<LoadGameplayState, GameLaunchConfig>(config, launchToken);
                _wizardCoordinator.CompleteStartAttempt(succeeded: true);
            }
            catch (OperationCanceledException)
            {
                viewModel.SetInteractable(true);

                if (_wizardCoordinator.IsActive)
                    _wizardCoordinator.CancelStartAttempt();
            }
            catch (Exception exception)
            {
                viewModel.SetInteractable(true);
                Log.Exception(exception, LogTags.UI);

                if (_wizardCoordinator.IsActive)
                {
                    _wizardCoordinator.CompleteStartAttempt(
                        succeeded: false,
                        error: CreateStartFailedError());
                }
            }
            finally
            {
                _launchCts?.Dispose();
                _launchCts = null;
                Interlocked.Exchange(ref _startInProgress, 0);
            }
        }

        private void HandleOnlineFlowSnapshot(OnlineFlowSnapshot snapshot)
        {
            var previousState = _hasOnlineFlowState ? _lastOnlineFlowState : snapshot.State;
            _lastOnlineFlowState = snapshot.State;
            _hasOnlineFlowState = true;

            if (_startInProgress == 0)
                return;

            if (ShouldCancelLaunchByOnlineTransition(previousState, snapshot.State))
                _launchCts?.Cancel();
        }

        private void HandleWizardAborted(AbortReason reason)
        {
            if (_viewModel == null || !ShouldRestoreMainMenu(reason))
                return;

            if (_startInProgress != 0)
                _launchCts?.Cancel();

            _overlayNavigator.ShowMainMenu(_viewModel);
        }

        private MainMenuViewModel RequireViewModel() =>
            _viewModel ?? throw new InvalidOperationException("MainMenuMatchStartFlow is not initialized.");

        private static WizardError CreateOnlinePreparationError() =>
            new(
                code: "wizard.online_prepare_failed",
                messageKey: "Errors.GameWizard.UnhandledException",
                isBlocking: true,
                displayType: ErrorDisplayType.Modal);

        private static WizardError CreateStartFailedError() =>
            new(
                code: "wizard.start_failed",
                messageKey: "Errors.GameWizard.UnhandledException",
                isBlocking: true,
                displayType: ErrorDisplayType.Modal);

        private static bool ShouldCancelLaunchByOnlineTransition(OnlineFlowState previousState, OnlineFlowState currentState)
        {
            if (currentState is OnlineFlowState.Terminated or OnlineFlowState.Failed)
                return true;

            return currentState == OnlineFlowState.Idle && IsActiveOnlineState(previousState);
        }

        private static bool IsActiveOnlineState(OnlineFlowState state) =>
            state is OnlineFlowState.HostIntentConfirmed
                or OnlineFlowState.HostStarting
                or OnlineFlowState.WaitingForPlayer
                or OnlineFlowState.GuestConnecting
                or OnlineFlowState.ConnectedCountdown
                or OnlineFlowState.InGame
                or OnlineFlowState.Result
                or OnlineFlowState.Reconnecting;

        private static bool ShouldRestoreMainMenu(AbortReason reason) =>
            reason is AbortReason.UserCancel
                or AbortReason.Error
                or AbortReason.StartCancelled
                or AbortReason.Disconnect;
    }
}