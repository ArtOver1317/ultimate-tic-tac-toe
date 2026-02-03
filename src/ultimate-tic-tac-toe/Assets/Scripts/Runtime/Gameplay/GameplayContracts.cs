using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Runtime.GameModes.Wizard;

namespace Runtime.Gameplay
{
    public interface IGameplayStartup
    {
        UniTask StartAsync(CancellationToken ct);
    }

    public interface IGameService : IDisposable
    {
        UniTask<IGameplaySession> StartMatchAsync(GameLaunchConfig config, CancellationToken ct);
    }

    public interface IGameplaySession : IDisposable
    {
        FieldRenderSpec FieldRenderSpec { get; }
    }

    public interface IGameplayFieldPresenter : IDisposable
    {
        UniTask BindAsync(FieldRenderSpec spec, CancellationToken ct);
        void Unbind();
    }
}
