#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Runtime.GameModes.Wizard.Matchmaking.Contracts;
using Runtime.GameModes.Wizard.Matchmaking.Runtime;
using Runtime.GameModes.Wizard.Session;

namespace Runtime.GameModes.Wizard.Coordinator
{
    internal sealed class GameWizardMatchmakingSearchFlow
    {
        private readonly GameWizardCoordinatorContext _context;
        private readonly GameWizardMatchmakingBindings _bindings;
        private readonly GameWizardMatchmakingFlow _owner;

        internal GameWizardMatchmakingSearchFlow(
            GameWizardCoordinatorContext context,
            GameWizardMatchmakingBindings bindings,
            GameWizardMatchmakingFlow owner)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        internal async UniTask OpenAsync(GameSessionSnapshot snapshot, CancellationToken ct)
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

        internal async UniTask RestartAsync(GameSessionSnapshot snapshot, CancellationToken ct)
        {
            if (!_bindings.HasActiveViewModel)
                return;

            if (!TryCreateMatchmakingRequest(snapshot, out var request))
            {
                await _owner.CloseMatchmakingToSetupAsync(ct);
                return;
            }

            _owner.UpdateMatchmakingResult(null);

            var started = await _bindings.ViewModel.TryBeginSearchAsync(request, ct);
            
            if (!started)
                _owner.ReportMatchmakingInlineError(WizardError.Codes.MatchmakingRestartFailed);
        }

        private bool TryCreateMatchmakingRequest(GameSessionSnapshot snapshot, out MatchmakingRequest request)
        {
            if (string.IsNullOrWhiteSpace(snapshot.SelectedGameId) || snapshot.GameConfig == null)
            {
                _context.Signals.TrySetCurrentError(CreateConfigRequiredError());
                request = null!;
                return false;
            }

            request = new MatchmakingRequest(snapshot.SelectedGameId, snapshot.GameConfig, snapshot.MoveTimeLimitSeconds);
            return true;
        }

        private async UniTask<QueueEntry?> TryEnterPreflightQueueAsync(MatchmakingRequest request, CancellationToken ct)
        {
            if (_context.MatchmakingService == null)
            {
                _owner.ReportMatchmakingInlineError(WizardError.Codes.MatchmakingStartFailed);
                return null;
            }

            try
            {
                var preflightQueueEntry = await _context.MatchmakingService.EnterQueueAsync(request, ct);
                
                if (preflightQueueEntry != null)
                    return preflightQueueEntry;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception)
            {
                _owner.ReportMatchmakingInlineError(WizardError.Codes.MatchmakingStartFailed);
                return null;
            }

            _owner.ReportMatchmakingInlineError(WizardError.Codes.MatchmakingStartFailed);
            return null;
        }

        private async UniTask OpenMatchmakingFromQueueEntryAsync(
            GameSessionSnapshot snapshot,
            QueueEntry preflightQueueEntry,
            CancellationToken ct)
        {
            var shouldCleanupPreflightQueue = true;

            try
            {
                if (await TryBeginSearchFromQueueEntryAsync(snapshot, preflightQueueEntry, ct)) 
                    shouldCleanupPreflightQueue = false;
            }
            finally
            {
                if (shouldCleanupPreflightQueue && _context.MatchmakingService != null)
                    BestEffortLeavePreflightQueueAsync(_context.MatchmakingService).Forget();
            }
        }

        private async UniTask<bool> TryBeginSearchFromQueueEntryAsync(
            GameSessionSnapshot snapshot,
            QueueEntry preflightQueueEntry,
            CancellationToken ct)
        {
            var searchStarted = false;

            await _context.TransitionAsync(
                transition: async token =>
                {
                    var viewModel = await OpenBoundMatchmakingViewAsync(snapshot, token);
                    searchStarted = await viewModel.TryBeginSearchFromQueueEntryAsync(preflightQueueEntry, token);

                    if (!searchStarted)
                        await ReturnToSetupAfterMatchmakingStartFailureAsync(token);
                },
                ct: ct);

            return searchStarted;
        }

        private async UniTask<MatchmakingViewModel> OpenBoundMatchmakingViewAsync(GameSessionSnapshot snapshot, CancellationToken ct)
        {
            var viewModel = await _context.Navigator.ReplaceMatchSetupWithMatchmakingAsync(ct);
            
            if (viewModel == null)
                throw new InvalidOperationException("Matchmaking ViewModel is not available.");

            _context.Step = GameWizardCoordinatorContext.WizardStep.Matchmaking;
            _bindings.Bind(viewModel, snapshot);
            return viewModel;
        }

        private async UniTask ReturnToSetupAfterMatchmakingStartFailureAsync(CancellationToken ct)
        {
            await _context.Navigator.ReplaceMatchmakingWithMatchSetupAsync(ct);
            _bindings.Cleanup();
            _owner.ReportMatchmakingInlineError(WizardError.Codes.MatchmakingStartFailed);
            _context.Step = GameWizardCoordinatorContext.WizardStep.MatchSetup;
        }

        private static WizardError CreateConfigRequiredError() => new(
            code: WizardError.Codes.ModeConfigRequired,
            messageKey: "Errors.GameWizard.ConfigRequired",
            isBlocking: false,
            displayType: ErrorDisplayType.Inline);

        private async UniTask BestEffortLeavePreflightQueueAsync(IMatchmakingService matchmakingService)
        {
            using var leaveCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

            try
            {
                await matchmakingService.LeaveAsync(leaveCts.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                GameWizardMatchmakingFlow.LogForgetException(ex);
            }
        }
    }
}