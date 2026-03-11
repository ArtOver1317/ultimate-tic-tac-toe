#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Runtime.GameModes.Wizard.Session;
using Runtime.Infrastructure.Logging;

namespace Runtime.GameModes.Wizard.Coordinator
{
    /// <summary>
    /// Start flow for launching the selected game and completing async start attempts.
    /// </summary>
    internal sealed class GameWizardLaunchFlow
    {
        private readonly GameWizardCoordinatorContext _context;
        private readonly GameWizardMatchmakingFlow _matchmakingFlow;
        private readonly GameWizardAbortFlow _abortFlow;

        internal GameWizardLaunchFlow(
            GameWizardCoordinatorContext context,
            GameWizardMatchmakingFlow matchmakingFlow,
            GameWizardAbortFlow abortFlow)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _matchmakingFlow = matchmakingFlow ?? throw new ArgumentNullException(nameof(matchmakingFlow));
            _abortFlow = abortFlow ?? throw new ArgumentNullException(nameof(abortFlow));
        }

        internal async UniTask HandleStartIntentAsync(CancellationToken ct)
        {
            if (_context.TryGetSessionSnapshot(out var snapshot) &&
                snapshot is { OpponentType: OpponentType.Human, HumanOpponentKind: HumanOpponentKind.Matchmaking })
            {
                await _matchmakingFlow.OpenAsync(snapshot, ct);
                return;
            }

            if (!_context.TryBuildLaunchConfig(out var launchConfig, out var error))
            {
                if (error != null)
                    _context.Signals.TrySetCurrentError(error);

                return;
            }

            _context.Signals.SetIsSubmitting(true);

            if (launchConfig != null)
                _context.Signals.PublishGameLaunchRequested(launchConfig);
        }

        internal void CompleteStartAttempt(bool succeeded, WizardError? error = null)
        {
            if (_context.IsDisposed)
                return;

            if (PlayerLoopHelper.IsMainThread)
            {
                CompleteStartAttemptOnMainThreadCoreAsync(succeeded, error).Forget(ex =>
                {
                    if (ex is OperationCanceledException || ex is ObjectDisposedException)
                        return;

                    GameLog.Exception(ex);
                });
                
                return;
            }

            CompleteStartAttemptOnMainThreadAsync(succeeded, error).Forget();
        }

        internal void CancelStartAttempt()
        {
            if (_context.IsDisposed)
                return;

            if (PlayerLoopHelper.IsMainThread)
            {
                _context.Signals.SetIsSubmitting(false);
                _context.Signals.ClearCurrentErrorValue();
                _matchmakingFlow.TryHandleTerminalModalAcknowledge();
                return;
            }

            CancelStartAttemptOnMainThreadAsync().Forget();
        }

        private async UniTask CompleteStartAttemptOnMainThreadCoreAsync(bool succeeded, WizardError? error)
        {
            if (!_context.IsActive)
            {
                _context.Signals.SetIsSubmitting(false);
                return;
            }

            if (!succeeded)
            {
                HandleFailedStartAttempt(error);
                return;
            }

            try
            {
                await _abortFlow.AbortAsync(AbortReason.GameStarted, awaitProcessingTask: false);
            }
            finally
            {
                _context.Signals.SetIsSubmitting(false);
            }
        }

        private void HandleFailedStartAttempt(WizardError? error)
        {
            _context.Signals.SetIsSubmitting(false);

            if (_context.Step == GameWizardCoordinatorContext.WizardStep.Matchmaking || _matchmakingFlow.HasActiveViewModel)
            {
                _matchmakingFlow.NotifySessionStartFailed();
                return;
            }

            _context.Signals.TrySetCurrentError(error ?? WizardError.FromException(new InvalidOperationException("Start failed.")));
        }

        private async UniTaskVoid CompleteStartAttemptOnMainThreadAsync(bool succeeded, WizardError? error)
        {
            try
            {
                await UniTask.SwitchToMainThread(_context.LifetimeToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (_context.IsDisposed)
                return;

            await CompleteStartAttemptOnMainThreadCoreAsync(succeeded, error);
        }

        private async UniTaskVoid CancelStartAttemptOnMainThreadAsync()
        {
            try
            {
                await UniTask.SwitchToMainThread(_context.LifetimeToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (_context.IsDisposed)
                return;

            _context.Signals.SetIsSubmitting(false);
            _context.Signals.ClearCurrentErrorValue();
            _matchmakingFlow.TryHandleTerminalModalAcknowledge();
        }
    }
}