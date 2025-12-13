using System.Threading;
using Cysharp.Threading.Tasks;
using Runtime.Infrastructure.GameStateMachine.States;

namespace Runtime.Infrastructure.GameStateMachine
{
    public interface IGameStateMachine
    {
        IExitableState CurrentState { get; }
        
        UniTask EnterAsync<TState>(CancellationToken cancellationToken = default) where TState : class, IState;
        UniTask EnterAsync<TState, TPayload>(TPayload payload, CancellationToken cancellationToken = default) where TState : class, IPayloadedState<TPayload>;
    }
}

