#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.Infrastructure.Logging;

namespace Runtime.GameModes.Wizard
{
    /// <summary>
    /// Matchmaking flow: open/close matchmaking step, bind ViewModel, handle state transitions.
    /// </summary>
    public sealed partial class GameWizardCoordinator
    {
        private async UniTask OpenMatchmakingAsync(GameSessionSnapshot snapshot, CancellationToken ct)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));

            if (string.IsNullOrWhiteSpace(snapshot.SelectedGameId) || snapshot.GameConfig == null)
            {
                TrySetCurrentError(new WizardError(
                    code: "wizard.mode_config_required",
                    messageKey: "Errors.GameWizard.ConfigRequired",
                    isBlocking: false,
                    displayType: ErrorDisplayType.Inline));
                
                return;
            }

            var request = new MatchmakingRequest(snapshot.SelectedGameId, snapshot.GameConfig, snapshot.MoveTimeLimitSeconds);

            if (_matchmakingService == null)
            {
                TrySetCurrentError(new WizardError(
                    code: "wizard.matchmaking_start_failed",
                    messageKey: "Errors.GameWizard.MatchmakingFailed",
                    isBlocking: false,
                    displayType: ErrorDisplayType.Inline));
                return;
            }

            QueueEntry? preflightQueueEntry = null;
            var shouldCleanupPreflightQueue = false;

            try
            {
                preflightQueueEntry = await _matchmakingService.EnterQueueAsync(request, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception)
            {
                TrySetCurrentError(new WizardError(
                    code: "wizard.matchmaking_start_failed",
                    messageKey: "Errors.GameWizard.MatchmakingFailed",
                    isBlocking: false,
                    displayType: ErrorDisplayType.Inline));
                return;
            }

            if (preflightQueueEntry == null)
            {
                TrySetCurrentError(new WizardError(
                    code: "wizard.matchmaking_start_failed",
                    messageKey: "Errors.GameWizard.MatchmakingFailed",
                    isBlocking: false,
                    displayType: ErrorDisplayType.Inline));
                return;
            }

            shouldCleanupPreflightQueue = true;

            try
            {
                await TransitionAsync(
                    transition: async token =>
                    {
                        var viewModel = await _navigator.ReplaceMatchSetupWithMatchmakingAsync(token);
                        
                        if (viewModel == null)
                            throw new InvalidOperationException("Matchmaking ViewModel is not available.");

                        _step = WizardStep.Matchmaking;

                        BindMatchmakingViewModel(viewModel, snapshot);

                        var started = preflightQueueEntry != null
                            ? await viewModel.TryBeginSearchFromQueueEntryAsync(preflightQueueEntry, token)
                            : await viewModel.TryBeginSearchAsync(request, token);

                        if (started)
                        {
                            shouldCleanupPreflightQueue = false;
                            return;
                        }

                        await _navigator.ReplaceMatchmakingWithMatchSetupAsync(token);
                        CleanupMatchmakingBindings();

                        TrySetCurrentError(new WizardError(
                            code: "wizard.matchmaking_start_failed",
                            messageKey: "Errors.GameWizard.MatchmakingFailed",
                            isBlocking: false,
                            displayType: ErrorDisplayType.Inline));

                        _step = WizardStep.MatchSetup;
                    },
                    ct: ct);
            }
            finally
            {
                if (shouldCleanupPreflightQueue)
                    BestEffortLeavePreflightQueueAsync(_matchmakingService).Forget();
            }
        }

        private void BindMatchmakingViewModel(MatchmakingViewModel viewModel, GameSessionSnapshot snapshot)
        {
            CleanupMatchmakingBindings();

            _matchmakingViewModel = viewModel;
            _matchmakingSubscriptions = new CompositeDisposable();

            viewModel.BackRequested
                .Subscribe(_ =>
                {
                    if (_matchmakingViewModel == null)
                        return;

                    var currentState = _matchmakingViewModel.State.CurrentValue;
                    if (currentState is MatchmakingState.Searching or MatchmakingState.CancelPending)
                        return;

                    CloseMatchmakingToSetupAsync(CancellationToken.None).Forget(LogForgetException);
                })
                .AddTo(_matchmakingSubscriptions);

            viewModel.RetryRequested
                .Subscribe(_ => TryRestartMatchmakingAsync(snapshot, CancellationToken.None).Forget(LogForgetException))
                .AddTo(_matchmakingSubscriptions);

            viewModel.State
                .Subscribe(state => HandleMatchmakingStateChanged(state, CancellationToken.None).Forget(LogForgetException))
                .AddTo(_matchmakingSubscriptions);

            viewModel.Result
                .Subscribe(UpdateMatchmakingResult)
                .AddTo(_matchmakingSubscriptions);

            UpdateMatchmakingResult(null);
        }

        private async UniTask TryRestartMatchmakingAsync(GameSessionSnapshot snapshot, CancellationToken ct)
        {
            if (_matchmakingViewModel == null)
                return;

            if (string.IsNullOrWhiteSpace(snapshot.SelectedGameId) || snapshot.GameConfig == null)
            {
                TrySetCurrentError(new WizardError(
                    code: "wizard.mode_config_required",
                    messageKey: "Errors.GameWizard.ConfigRequired",
                    isBlocking: false,
                    displayType: ErrorDisplayType.Inline));

                CloseMatchmakingToSetupAsync(ct).Forget(LogForgetException);
                return;
            }

            var request = new MatchmakingRequest(snapshot.SelectedGameId, snapshot.GameConfig, snapshot.MoveTimeLimitSeconds);
            UpdateMatchmakingResult(null);

            var started = await _matchmakingViewModel.TryBeginSearchAsync(request, ct);
            if (!started)
            {
                TrySetCurrentError(new WizardError(
                    code: "wizard.matchmaking_restart_failed",
                    messageKey: "Errors.GameWizard.MatchmakingFailed",
                    isBlocking: false,
                    displayType: ErrorDisplayType.Inline));
            }
        }

        private async UniTask HandleMatchmakingStateChanged(MatchmakingState state, CancellationToken ct)
        {
            if (_step != WizardStep.Matchmaking)
                return;

            switch (state)
            {
                case MatchmakingState.CancelPending:
                    break;

                case MatchmakingState.Found:
                    await UniTask.Delay(_matchmakingFoundAutoCloseDelay, cancellationToken: ct);
                    await CloseMatchmakingAndStartAsync(ct);
                    break;

                case MatchmakingState.Cancelled:
                    await CloseMatchmakingToSetupAsync(ct);
                    break;

                case MatchmakingState.TerminalModal:
                    Interlocked.Exchange(ref _matchmakingTerminalModalPendingAck, 1);
                    var terminalFailureKey = _matchmakingViewModel?.Failure.CurrentValue?.MessageKey ?? "Errors.GameWizard.MatchmakingFailed";
                    TrySetCurrentError(new WizardError(
                        code: "wizard.matchmaking_terminal",
                        messageKey: terminalFailureKey,
                        isBlocking: true,
                        displayType: ErrorDisplayType.Modal));
                    break;
            }
        }

        private async UniTask CloseMatchmakingToSetupAsync(CancellationToken ct)
        {
            if (_step != WizardStep.Matchmaking)
                return;

            if (Interlocked.Exchange(ref _matchmakingCloseInProgress, 1) != 0)
                return;

            try
            {
                await TransitionAsync(
                    transition: _navigator.ReplaceMatchmakingWithMatchSetupAsync,
                    ct: ct);

                CleanupMatchmakingBindings();
                _step = WizardStep.MatchSetup;
            }
            finally
            {
                Interlocked.Exchange(ref _matchmakingCloseInProgress, 0);
            }
        }

        private async UniTask CloseMatchmakingAndStartAsync(CancellationToken ct)
        {
            if (_step != WizardStep.Matchmaking)
                return;

            if (Interlocked.Exchange(ref _matchmakingCloseInProgress, 1) != 0)
                return;

            try
            {
                if (!TryBuildLaunchConfig(out var launchConfig, out var error))
                {
                    if (error != null)
                        TrySetCurrentError(error);

                    await TransitionAsync(
                        transition: _navigator.ReplaceMatchmakingWithMatchSetupAsync,
                        ct: ct);

                    CleanupMatchmakingBindings();
                    _step = WizardStep.MatchSetup;
                    return;
                }

                await TransitionAsync(
                    transition: _navigator.CloseMatchmakingAsync,
                    ct: ct);

                SetIsSubmitting(true);

                if (launchConfig != null) 
                    PublishGameLaunchRequested(launchConfig);
            }
            finally
            {
                Interlocked.Exchange(ref _matchmakingCloseInProgress, 0);
            }
        }

        private void CleanupMatchmakingBindings()
        {
            _matchmakingSubscriptions?.Dispose();
            _matchmakingSubscriptions = null;
            _matchmakingViewModel = null;
            UpdateMatchmakingResult(null);
            Interlocked.Exchange(ref _matchmakingCloseInProgress, 0);
        }

        private void TryHandleMatchmakingTerminalModalAcknowledge()
        {
            if (Interlocked.Exchange(ref _matchmakingTerminalModalPendingAck, 0) == 0)
                return;

            _matchmakingViewModel?.AcknowledgeTerminalModal();
            CloseMatchmakingToSetupAsync(CancellationToken.None).Forget(LogForgetException);
        }

        private static async UniTask BestEffortLeavePreflightQueueAsync(IMatchmakingService matchmakingService)
        {
            using var leaveCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

            try
            {
                await matchmakingService.LeaveAsync(leaveCts.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                LogForgetException(ex);
            }
        }

        private void UpdateMatchmakingResult(MatchmakingResult? result) =>
            _session?.Update(s =>
                s.WithMatchmakingResult(result?.MatchId, result?.OpponentId, result?.IsHost ?? false));

        private static void LogForgetException(Exception ex)
        {
            if (ex is OperationCanceledException)
                return;

            GameLog.Exception(ex);
        }
    }
}
