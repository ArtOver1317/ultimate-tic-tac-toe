#nullable enable

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.Matchmaking.Contracts;
using Runtime.GameModes.Wizard.Online;
using Runtime.GameModes.Wizard.Session;

namespace Runtime.GameModes.Wizard.Coordinator
{
    /// <summary>
    /// Coordinator owning wizard startup, intent serialization and interaction between lifecycle, launch and matchmaking flows.
    /// </summary>
    public sealed class GameWizardCoordinator : IGameWizardCoordinator, IDisposable
    {
        private readonly GameWizardCoordinatorContext _context;
        private readonly GameWizardMatchmakingFlow _matchmakingFlow;
        private readonly GameWizardAbortFlow _abortFlow;
        private readonly GameWizardLaunchFlow _launchFlow;
        private readonly GameWizardIntentFlow _intentFlow;

        public GameWizardCoordinator(
            IGameWizardNavigator navigator,
            Func<IGameSession> sessionFactory,
            IOnlineSessionFlowService? onlineSessionFlow = null,
            IMatchmakingService? matchmakingService = null)
        {
            _context = new GameWizardCoordinatorContext(
                navigator,
                sessionFactory,
                onlineSessionFlow ?? NoOpOnlineSessionFlowService.Instance,
                matchmakingService);

            _matchmakingFlow = new GameWizardMatchmakingFlow(_context);
            _abortFlow = new GameWizardAbortFlow(_context, _matchmakingFlow);
            _launchFlow = new GameWizardLaunchFlow(_context, _matchmakingFlow, _abortFlow);
            _intentFlow = new GameWizardIntentFlow(_context, _abortFlow, _launchFlow);
        }

        public ReadOnlyReactiveProperty<bool> IsTransitioning => _context.IsTransitioning;
        public ReadOnlyReactiveProperty<bool> IsSubmitting => _context.IsSubmitting;
        public ReadOnlyReactiveProperty<WizardError?> CurrentError => _context.CurrentError;
        public Observable<GameLaunchConfig> GameLaunchRequested => _context.GameLaunchRequested;
        public Observable<AbortReason> WizardAborted => _context.WizardAborted;
        public IGameSession Session => _context.Session;
        public bool IsActive => _context.IsActive;

        public async UniTask StartWizardAsync(CancellationToken ct)
        {
            _context.EnsureNotDisposed();

            await UniTask.SwitchToMainThread(ct);

            if (!_context.TryInitializeWizardSession(ct, _intentFlow.ProcessAsync, out var wizardToken))
                return;

            await OpenFirstStepAsync(wizardToken);
        }

        public UniTask AbortWizardAsync(AbortReason reason)
        {
            _context.EnsureNotDisposed();
            return _abortFlow.AbortAsync(reason, awaitProcessingTask: !_context.IsInProcessingLoop.Value);
        }

        public void Dispose() => _abortFlow.Dispose();

        public void CompleteStartAttempt(bool succeeded, WizardError? error = null) =>
            _launchFlow.CompleteStartAttempt(succeeded, error);

        public void CancelStartAttempt() =>
            _launchFlow.CancelStartAttempt();

        public void ClearCurrentError()
        {
            if (_context.IsDisposed)
                return;

            _context.Signals.ClearCurrentError(_matchmakingFlow.TryHandleTerminalModalAcknowledge);
        }

        public bool TryGetSession([NotNullWhen(true)] out IGameSession? session) =>
            _context.TryGetSession(out session);

        public bool TryPublishIntent(WizardIntent intent) =>
            _intentFlow.TryPublishIntent(intent);

        private async UniTask OpenFirstStepAsync(CancellationToken wizardToken)
        {
            try
            {
                await _context.Navigator.OpenModeSelectionAsync(wizardToken);
                _context.MarkModeSelectionOpened();
            }
            catch (OperationCanceledException)
            {
                await _abortFlow.AbortAsync(AbortReason.StartCancelled, awaitProcessingTask: false);
                throw;
            }
            catch
            {
                await _abortFlow.AbortAsync(AbortReason.Error, awaitProcessingTask: false);
                throw;
            }
        }
    }
}
