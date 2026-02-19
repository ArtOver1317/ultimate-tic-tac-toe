using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Runtime.GameModes.Wizard;
using Runtime.Gameplay;
using Runtime.Infrastructure;
using Runtime.Infrastructure.GameStateMachine;
using Runtime.Infrastructure.GameStateMachine.States;
using R3;

namespace Tests.EditMode.Gameplay
{
    [TestFixture]
    [Category("Unit")]
    public class GameplayBackHandlerTests
    {
        [Test]
        public async Task WhenHandleBackForOnlineSession_ThenExitsOnlineFlowAndTransitionsToMainMenu()
        {
            var stateMachine = new SpyGameStateMachine();

            var entryModeStore = new MainMenuEntryModeStore();
            var onlineFlow = new SpyOnlineSessionFlowService();

            var onlineSessionContext = new OnlineGameplaySessionContextStore();
            onlineSessionContext.SetDirectInviteSession("ABCDEF", "local-user", isHost: true);

            var sut = new GameplayBackHandler(stateMachine, entryModeStore, onlineFlow, onlineSessionContext);

            await sut.HandleBackAsync(CancellationToken.None);

            onlineFlow.ExitCalls.Should().Be(1);
            stateMachine.LoadMainMenuEnterCalls.Should().Be(1);

            entryModeStore.TryConsume(out var entryMode).Should().BeTrue();
            entryMode.Should().Be(MainMenuEntryMode.OpenWizard);
        }

        [Test]
        public async Task WhenHandleBackForOfflineSession_ThenSkipsOnlineExitAndTransitionsToMainMenu()
        {
            var stateMachine = new SpyGameStateMachine();

            var entryModeStore = new MainMenuEntryModeStore();
            var onlineFlow = new SpyOnlineSessionFlowService();

            var onlineSessionContext = new OnlineGameplaySessionContextStore();

            var sut = new GameplayBackHandler(stateMachine, entryModeStore, onlineFlow, onlineSessionContext);

            await sut.HandleBackAsync(CancellationToken.None);

            onlineFlow.ExitCalls.Should().Be(0);
            stateMachine.LoadMainMenuEnterCalls.Should().Be(1);

            entryModeStore.TryConsume(out var entryMode).Should().BeTrue();
            entryMode.Should().Be(MainMenuEntryMode.OpenWizard);
        }

        private sealed class SpyGameStateMachine : IGameStateMachine
        {
            public IExitableState CurrentState => null!;
            public int LoadMainMenuEnterCalls { get; private set; }

            public UniTask EnterAsync<TState>(CancellationToken cancellationToken = default) where TState : class, IState
            {
                if (typeof(TState) == typeof(LoadMainMenuState))
                    LoadMainMenuEnterCalls++;

                return UniTask.CompletedTask;
            }

            public UniTask EnterAsync<TState, TPayload>(TPayload payload, CancellationToken cancellationToken = default) where TState : class, IPayloadedState<TPayload> =>
                UniTask.CompletedTask;
        }

        private sealed class SpyOnlineSessionFlowService : IOnlineSessionFlowService
        {
            private readonly ReactiveProperty<OnlineFlowSnapshot> _snapshot = new(
                new OnlineFlowSnapshot(
                    OnlineFlowState.Idle,
                    previousStableState: null,
                    candidateSessionId: string.Empty,
                    activeSessionId: null,
                    flowEpoch: 1,
                    region: "eu",
                    canStart: false,
                    isBusy: false,
                    errorCode: OnlineErrorCode.None,
                    errorLocalizationKey: null,
                    statusLocalizationKey: null,
                    countdownRemainingSeconds: null,
                    graceDeadlineUtc: null));

            public int ExitCalls { get; private set; }

            public ReadOnlyReactiveProperty<OnlineFlowSnapshot> Snapshot => _snapshot;

            public UniTask EnterHumanSetupAsync(string region, string currentUserId) => UniTask.CompletedTask;
            public UniTask ConfirmHostIntentAsync() => UniTask.CompletedTask;
            public UniTask StartHostSessionAsync(OnlineSessionConfig hostConfig) => UniTask.CompletedTask;
            public UniTask JoinBySessionIdAsync(string rawSessionIdInput, string region, string currentUserId) => UniTask.CompletedTask;
            public UniTask CopyVisibleSessionIdAsync() => UniTask.CompletedTask;
            public UniTask BackAsync() => UniTask.CompletedTask;

            public UniTask ExitAsync()
            {
                ExitCalls++;
                return UniTask.CompletedTask;
            }

            public UniTask SetReadyForNextMatchAsync(bool isReady) => UniTask.CompletedTask;
            public UniTask OnOpponentReadyForNextMatchAsync(bool isReady) => UniTask.CompletedTask;
            public UniTask OnHostCreatedAsync() => UniTask.CompletedTask;
            public UniTask OnJoinSucceededAsync() => UniTask.CompletedTask;
            public UniTask OnJoinFailedAsync(OnlineErrorCode errorCode) => UniTask.CompletedTask;
            public UniTask OnGuestJoinedAsync() => UniTask.CompletedTask;
            public UniTask OnCountdownTickAsync(int remainingSeconds) => UniTask.CompletedTask;
            public UniTask OnGameplayEnteredAsync() => UniTask.CompletedTask;
            public UniTask OnRoundCompletedAsync() => UniTask.CompletedTask;
            public UniTask OnDisconnectDetectedAsync() => UniTask.CompletedTask;
            public UniTask OnReconnectSucceededAsync() => UniTask.CompletedTask;
            public UniTask OnGraceTimeoutAsync(int eventEpoch) => UniTask.CompletedTask;
            public UniTask OnOpponentLeftAsync() => UniTask.CompletedTask;

            public void Dispose() => _snapshot.Dispose();
        }
    }
}
