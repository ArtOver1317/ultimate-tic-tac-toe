using System.Threading;
using Cysharp.Threading.Tasks;
using Runtime.Infrastructure.Logging;
using Runtime.Infrastructure.GameStateMachine;
using Runtime.Infrastructure.GameStateMachine.States;
using StripLog;
using VContainer.Unity;

namespace Runtime.Infrastructure.EntryPoint
{
    public class GameEntryPoint : IAsyncStartable
    {
        private readonly IGameStateMachine _stateMachine;

        public GameEntryPoint(IGameStateMachine stateMachine) => _stateMachine = stateMachine;

        public async UniTask StartAsync(CancellationToken cancellationToken)
        {
            Log.Debug(LogTags.Infrastructure, "[GameEntryPoint] Starting game...");
            await _stateMachine.EnterAsync<BootstrapState>(cancellationToken);
        }
    }
}