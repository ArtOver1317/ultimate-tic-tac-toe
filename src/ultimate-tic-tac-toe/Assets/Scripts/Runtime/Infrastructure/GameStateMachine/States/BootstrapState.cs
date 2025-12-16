using System.Threading;
using Cysharp.Threading.Tasks;
using Runtime.Infrastructure.Logging;
using StripLog;

namespace Runtime.Infrastructure.GameStateMachine.States
{
    public class BootstrapState : IState
    {
        private readonly IGameStateMachine _stateMachine;

        public BootstrapState(IGameStateMachine stateMachine) => _stateMachine = stateMachine;

        public async UniTask EnterAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Log.Debug(LogTags.Infrastructure, "[BootstrapState] Initializing...");
            await _stateMachine.EnterAsync<LoadMainMenuState>(cancellationToken);
        }

        public void Exit() => Log.Debug(LogTags.Infrastructure, "[BootstrapState] Exiting...");
    }
}

