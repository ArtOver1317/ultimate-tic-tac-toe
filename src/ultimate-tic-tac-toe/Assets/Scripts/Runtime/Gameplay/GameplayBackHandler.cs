using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Runtime.Infrastructure;
using Runtime.Infrastructure.GameStateMachine;
using Runtime.Infrastructure.GameStateMachine.States;

namespace Runtime.Gameplay
{
    public interface IGameplayBackHandler
    {
        UniTask HandleBackAsync(CancellationToken ct);
    }

    public sealed class GameplayBackHandler : IGameplayBackHandler
    {
        private readonly IGameStateMachine _stateMachine;
        private readonly IMainMenuEntryModeStore _entryModeStore;

        public GameplayBackHandler(
            IGameStateMachine stateMachine,
            IMainMenuEntryModeStore entryModeStore)
        {
            _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
            _entryModeStore = entryModeStore ?? throw new ArgumentNullException(nameof(entryModeStore));
        }

        public async UniTask HandleBackAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _entryModeStore.Set(MainMenuEntryMode.OpenWizard);
            await _stateMachine.EnterAsync<LoadMainMenuState>(ct);
        }
    }
}
