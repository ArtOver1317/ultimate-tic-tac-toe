#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.Matchmaking.Contracts;
using Runtime.GameModes.Wizard.Session;
using Runtime.Infrastructure.Logging;

namespace Runtime.GameModes.Wizard.Coordinator
{
    /// <summary>
    /// Matchmaking-specific wizard flow, including view-model binding and return-to-setup handling.
    /// </summary>
    internal sealed class GameWizardMatchmakingFlow
    {
        private static readonly TimeSpan _matchmakingFoundAutoCloseDelay = TimeSpan.FromMilliseconds(450);

        private readonly GameWizardCoordinatorContext _context;
        private readonly GameWizardMatchmakingBindings _bindings;
        private readonly GameWizardMatchmakingSearchFlow _searchFlow;

        private int _closeInProgress;

        internal GameWizardMatchmakingFlow(GameWizardCoordinatorContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));

            _bindings = new GameWizardMatchmakingBindings(this);

            _searchFlow = new GameWizardMatchmakingSearchFlow(
                context,
                _bindings,
                this);
        }

        internal bool HasActiveViewModel => _bindings.HasActiveViewModel;

        internal void NotifySessionStartFailed() =>
            _bindings.NotifySessionStartFailed();

        internal UniTask OpenAsync(GameSessionSnapshot snapshot, CancellationToken ct) =>
            _searchFlow.OpenAsync(snapshot, ct);

        internal void CleanupBindings()
        {
            _bindings.Cleanup();
            Interlocked.Exchange(ref _closeInProgress, 0);
        }

        internal void TryHandleTerminalModalAcknowledge() =>
            _bindings.TryHandleTerminalModalAcknowledge();

        internal UniTask RestartMatchmakingAsync(GameSessionSnapshot snapshot, CancellationToken ct) =>
            _searchFlow.RestartAsync(snapshot, ct);

        internal async UniTask HandleMatchmakingStateChanged(MatchmakingState state, CancellationToken ct)
        {
            if (_context.Step != GameWizardCoordinatorContext.WizardStep.Matchmaking)
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

        private async UniTask HandleMatchmakingFoundAsync(CancellationToken ct)
        {
            await UniTask.Delay(_matchmakingFoundAutoCloseDelay, cancellationToken: ct);
            await CloseMatchmakingAndStartAsync(ct);
        }

        private void ShowMatchmakingTerminalError()
        {
            _bindings.MarkTerminalModalPending();

            var terminalFailureKey = _bindings.GetTerminalFailureMessageKey("Errors.GameWizard.MatchmakingFailed");

            _context.Signals.TrySetCurrentError(CreateMatchmakingError(
                code: WizardError.Codes.MatchmakingTerminal,
                messageKey: terminalFailureKey,
                isBlocking: true,
                displayType: ErrorDisplayType.Modal));
        }

        internal void ReportMatchmakingInlineError(string code) =>
            _context.Signals.TrySetCurrentError(CreateMatchmakingError(
                code: code,
                messageKey: "Errors.GameWizard.MatchmakingFailed",
                isBlocking: false,
                displayType: ErrorDisplayType.Inline));

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

        internal async UniTask CloseMatchmakingToSetupAsync(CancellationToken ct)
        {
            if (_context.Step != GameWizardCoordinatorContext.WizardStep.Matchmaking)
                return;

            if (Interlocked.Exchange(ref _closeInProgress, 1) != 0)
                return;

            try
            {
                await _context.TransitionAsync(
                    transition: _context.Navigator.ReplaceMatchmakingWithMatchSetupAsync,
                    ct: ct);

                CleanupBindings();
                _context.Step = GameWizardCoordinatorContext.WizardStep.MatchSetup;
            }
            finally
            {
                Interlocked.Exchange(ref _closeInProgress, 0);
            }
        }

        private async UniTask CloseMatchmakingAndStartAsync(CancellationToken ct)
        {
            if (_context.Step != GameWizardCoordinatorContext.WizardStep.Matchmaking)
                return;

            if (Interlocked.Exchange(ref _closeInProgress, 1) != 0)
                return;

            try
            {
                var launchConfig = await TryCloseMatchmakingForStartAsync(ct);
                
                if (launchConfig == null)
                    return;

                _context.Signals.SetIsSubmitting(true);
                _context.Signals.PublishGameLaunchRequested(launchConfig);
            }
            finally
            {
                Interlocked.Exchange(ref _closeInProgress, 0);
            }
        }

        private async UniTask<GameLaunchConfig?> TryCloseMatchmakingForStartAsync(CancellationToken ct)
        {
            if (!_context.TryBuildLaunchConfig(out var builtLaunchConfig, out var error))
            {
                await ReturnToSetupAfterInvalidLaunchConfigAsync(ct, error);
                return null;
            }

            await _context.TransitionAsync(
                transition: _context.Navigator.CloseMatchmakingAsync,
                ct: ct);

            return builtLaunchConfig;
        }

        private async UniTask ReturnToSetupAfterInvalidLaunchConfigAsync(CancellationToken ct, WizardError? error)
        {
            if (error != null)
                _context.Signals.TrySetCurrentError(error);

            await _context.TransitionAsync(
                transition: _context.Navigator.ReplaceMatchmakingWithMatchSetupAsync,
                ct: ct);

            CleanupBindings();
            _context.Step = GameWizardCoordinatorContext.WizardStep.MatchSetup;
        }
       
        internal void UpdateMatchmakingResult(MatchmakingResult? result) =>
            _context.UpdateSession(snapshot =>
                snapshot.WithMatchmakingResult(result?.MatchId, result?.OpponentId, result?.IsHost ?? false));

        internal static void LogForgetException(Exception ex)
        {
            if (ex is OperationCanceledException)
                return;

            GameLog.Exception(ex);
        }
    }
}