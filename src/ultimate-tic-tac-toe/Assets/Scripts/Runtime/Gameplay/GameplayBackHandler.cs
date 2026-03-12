using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Runtime.GameModes.Wizard.Online;
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
        private readonly IOnlineSessionFlowService _onlineSessionFlow;
        private readonly IOnlineGameplaySessionContextStore _onlineSessionContext;

        public GameplayBackHandler(
            IGameStateMachine stateMachine,
            IMainMenuEntryModeStore entryModeStore,
            IOnlineSessionFlowService onlineSessionFlow = null,
            IOnlineGameplaySessionContextStore onlineSessionContext = null)
        {
            _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
            _entryModeStore = entryModeStore ?? throw new ArgumentNullException(nameof(entryModeStore));
            _onlineSessionFlow = onlineSessionFlow ?? NoOpOnlineSessionFlowService.Instance;
            _onlineSessionContext = onlineSessionContext ?? new OnlineGameplaySessionContextStore();
        }

        public async UniTask HandleBackAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            if (_onlineSessionContext.Snapshot.IsOnlineDirectInvite)
                await _onlineSessionFlow.ExitAsync();

            _entryModeStore.Set(MainMenuEntryMode.OpenWizard);
            await _stateMachine.EnterAsync<LoadMainMenuState>(ct);
        }
    }
}
