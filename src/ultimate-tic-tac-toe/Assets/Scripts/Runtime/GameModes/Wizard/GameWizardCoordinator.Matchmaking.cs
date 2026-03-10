#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.GameModes.Wizard.Matchmaking;
using Runtime.GameModes.Wizard.Matchmaking.Contracts;
using Runtime.GameModes.Wizard.Matchmaking.Runtime;
using Runtime.GameModes.Wizard.Session;
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

            if (!TryCreateMatchmakingRequest(snapshot, out var request))
                return;

            var preflightQueueEntry = await TryEnterPreflightQueueAsync(request, ct);
            if (preflightQueueEntry == null)
                return;

            await OpenMatchmakingFromQueueEntryAsync(snapshot, preflightQueueEntry, ct);
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

            if (!TryCreateMatchmakingRequest(snapshot, out var request))
            {
                CloseMatchmakingToSetupAsync(ct).Forget(LogForgetException);
                return;
            }

            UpdateMatchmakingResult(null);

            var started = await _matchmakingViewModel.TryBeginSearchAsync(request, ct);
            if (!started)
                ReportMatchmakingInlineError("wizard.matchmaking_restart_failed");
        }

        private async UniTask HandleMatchmakingStateChanged(MatchmakingState state, CancellationToken ct)
        {
            if (_step != WizardStep.Matchmaking)
                return;

            switch (state)
            {
                case MatchmakingState.Found:
                    await HandleMatchmakingFoundAsync(ct);
                    break;

                case MatchmakingState.Cancelled:
                    await CloseMatchmakingToSetupAsync(ct);
                    break;

                case MatchmakingState.TerminalModal:
                    ShowMatchmakingTerminalError();
                    break;
            }
        }

        private async UniTask OpenMatchmakingFromQueueEntryAsync(
            GameSessionSnapshot snapshot,
            QueueEntry preflightQueueEntry,
            CancellationToken ct)
        {
            var shouldCleanupPreflightQueue = true;

            try
            {
                await TransitionAsync(
                    transition: async token =>
                    {
                        var viewModel = await OpenBoundMatchmakingViewAsync(snapshot, token);
                        if (await viewModel.TryBeginSearchFromQueueEntryAsync(preflightQueueEntry, token))
                        {
                            shouldCleanupPreflightQueue = false;
                            return;
                        }

                        await ReturnToSetupAfterMatchmakingStartFailureAsync(token);
                    },
                    ct: ct);
            }
            finally
            {
                if (shouldCleanupPreflightQueue && _matchmakingService != null)
                    BestEffortLeavePreflightQueueAsync(_matchmakingService).Forget();
            }
        }

        private async UniTask<MatchmakingViewModel> OpenBoundMatchmakingViewAsync(GameSessionSnapshot snapshot, CancellationToken ct)
        {
            var viewModel = await _navigator.ReplaceMatchSetupWithMatchmakingAsync(ct);
            if (viewModel == null)
                throw new InvalidOperationException("Matchmaking ViewModel is not available.");

            _step = WizardStep.Matchmaking;
            BindMatchmakingViewModel(viewModel, snapshot);
            return viewModel;
        }

        private async UniTask ReturnToSetupAfterMatchmakingStartFailureAsync(CancellationToken ct)
        {
            await _navigator.ReplaceMatchmakingWithMatchSetupAsync(ct);
            CleanupMatchmakingBindings();
            ReportMatchmakingInlineError("wizard.matchmaking_start_failed");
            _step = WizardStep.MatchSetup;
        }

        private bool TryCreateMatchmakingRequest(GameSessionSnapshot snapshot, out MatchmakingRequest request)
        {
            if (string.IsNullOrWhiteSpace(snapshot.SelectedGameId) || snapshot.GameConfig == null)
            {
                TrySetCurrentError(CreateConfigRequiredError());
                request = null!;
                return false;
            }

            request = new MatchmakingRequest(snapshot.SelectedGameId, snapshot.GameConfig, snapshot.MoveTimeLimitSeconds);
            return true;
        }

        private async UniTask<QueueEntry?> TryEnterPreflightQueueAsync(MatchmakingRequest request, CancellationToken ct)
        {
            if (_matchmakingService == null)
            {
                ReportMatchmakingInlineError("wizard.matchmaking_start_failed");
                return null;
            }

            try
            {
                var preflightQueueEntry = await _matchmakingService.EnterQueueAsync(request, ct);
                if (preflightQueueEntry != null)
                    return preflightQueueEntry;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception)
            {
                ReportMatchmakingInlineError("wizard.matchmaking_start_failed");
                return null;
            }

            ReportMatchmakingInlineError("wizard.matchmaking_start_failed");
            return null;
        }

        private async UniTask HandleMatchmakingFoundAsync(CancellationToken ct)
        {
            await UniTask.Delay(_matchmakingFoundAutoCloseDelay, cancellationToken: ct);
            await CloseMatchmakingAndStartAsync(ct);
        }

        private void ShowMatchmakingTerminalError()
        {
            Interlocked.Exchange(ref _matchmakingTerminalModalPendingAck, 1);

            var terminalFailureKey = _matchmakingViewModel?.Failure.CurrentValue?.MessageKey
                ?? "Errors.GameWizard.MatchmakingFailed";

            TrySetCurrentError(CreateMatchmakingError(
                code: "wizard.matchmaking_terminal",
                messageKey: terminalFailureKey,
                isBlocking: true,
                displayType: ErrorDisplayType.Modal));
        }

        private void ReportMatchmakingInlineError(string code) =>
            TrySetCurrentError(CreateMatchmakingError(
                code: code,
                messageKey: "Errors.GameWizard.MatchmakingFailed",
                isBlocking: false,
                displayType: ErrorDisplayType.Inline));

        private static WizardError CreateConfigRequiredError() => new(
            code: "wizard.mode_config_required",
            messageKey: "Errors.GameWizard.ConfigRequired",
            isBlocking: false,
            displayType: ErrorDisplayType.Inline);

        private static WizardError CreateMatchmakingError(
            string code,
            string messageKey,
            bool isBlocking,
            ErrorDisplayType displayType) =>
            new(
                code: code,
                messageKey: messageKey,
                isBlocking: isBlocking,
                displayType: displayType);

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
            var leaveTimeout = TimeSpan.FromSeconds(15);
            using var leaveCts = new CancellationTokenSource(leaveTimeout);

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
