using System.Threading;
using Cysharp.Threading.Tasks;

namespace Runtime.Infrastructure.GameStateMachine.States
{
    public interface IExitableState
    {
        void Exit();
    }

    public interface IState : IExitableState
    {
        UniTask EnterAsync(CancellationToken cancellationToken = default);
    }

    public interface IPayloadedState<in TPayload> : IExitableState
    {
        UniTask EnterAsync(TPayload payload, CancellationToken cancellationToken = default);
    }
}

