using System.Threading;
using Cysharp.Threading.Tasks;
using Runtime.Infrastructure.Logging;
using StripLog;

namespace Runtime.Infrastructure.GameStateMachine.States
{
    public class GameplayState : IState
    {
        private readonly IGameStateMachine _stateMachine;

        public GameplayState(IGameStateMachine stateMachine) => _stateMachine = stateMachine;

        public UniTask EnterAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Log.Debug(LogTags.Infrastructure, "[GameplayState] Game started");
            return UniTask.CompletedTask;
        }

        public void Exit() => Log.Debug(LogTags.Infrastructure, "[GameplayState] Game ended");

        public UniTask ReturnToMainMenuAsync(CancellationToken cancellationToken = default) =>
            _stateMachine.EnterAsync<LoadMainMenuState>(cancellationToken);
    }
}

