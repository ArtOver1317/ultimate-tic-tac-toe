using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using R3;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.Modes;
using Runtime.GameModes.Wizard.Online;
using Runtime.Gameplay;
using Runtime.Gameplay.Shared;
using Runtime.Games.TicTacToe.AI.Ultimate.Core;
using RulesGameStatus = Runtime.Gameplay.GameStatus;

namespace Tests.EditMode.Games.TicTacToe.Startup
{
    public partial class GameplayStartupTests
    {
        [Test]
        public async Task WhenOnlineMatchConfigPayloadExists_ThenUsesHostBoardSizeForStartMatch()
        {
            var onlineSessionStore = new OnlineGameplaySessionContextStore();
            onlineSessionStore.SetDirectInviteSession("ABCDEF", "guest-user", isHost: false);
            onlineSessionStore.SetMatchConfig(new OnlineMatchConfigPayload("classic", boardSize: 5, isUltimate: false, moveTimeLimitSeconds: 20));

            var networkBridge = Substitute.For<IGameplayNetworkBridge>();
            networkBridge.Snapshot.Returns(new ReactiveProperty<GameplayNetworkSnapshot?>(null));
            networkBridge.IncomingMoves.Returns(new Subject<MoveCommand>());
            networkBridge.IncomingRoundReadySignals.Returns(new Subject<RoundReadySignal>());
            networkBridge.IncomingTimeoutSignals.Returns(new Subject<OnlineTimeoutSignal>());
            networkBridge.BindAsync(Arg.Any<string>(), Arg.Any<bool>()).Returns(UniTask.CompletedTask);

            var onlineFlow = new TestOnlineSessionFlow();
            var matchStateProvider = Substitute.For<IMatchStateProvider>();

            var sut = CreateStartup(
                Substitute.For<IMatchFailSafeGateway>(),
                networkBridge: networkBridge,
                onlineSessionContextStore: onlineSessionStore,
                matchStateProvider: matchStateProvider,
                onlineSessionFlow: onlineFlow,
                statisticsReporter: CreateStatisticsReporter(_configStore, _eventStream));

            await sut.StartAsync(CancellationToken.None);

            await _gameService.Received(1).StartMatchAsync(
                Arg.Is<GameLaunchConfig>(cfg =>
                    cfg.GameConfig is TicTacToeConfig &&
                    ((TicTacToeConfig)cfg.GameConfig).BoardSize == 5 &&
                    !((TicTacToeConfig)cfg.GameConfig).IsUltimate &&
                    cfg.MoveTimeLimitSeconds == 20),
                Arg.Any<CancellationToken>());

            sut.Dispose();
        }

        [Test]
        public async Task WhenOnlineFlowTerminatedByOpponentLeftDuringOnlineMatch_ThenShowsResultAndRecordsLocalWin()
        {
            var onlineFlow = new TestOnlineSessionFlow();
            var onlineSessionStore = new OnlineGameplaySessionContextStore();
            onlineSessionStore.SetDirectInviteSession("ABCDEF", "local-user", isHost: false);

            var matchStateProvider = Substitute.For<IMatchStateProvider>();
            var networkBridge = Substitute.For<IGameplayNetworkBridge>();
            networkBridge.Snapshot.Returns(new ReactiveProperty<GameplayNetworkSnapshot?>(null));
            networkBridge.IncomingMoves.Returns(new Subject<MoveCommand>());
            networkBridge.IncomingRoundReadySignals.Returns(new Subject<RoundReadySignal>());
            networkBridge.IncomingTimeoutSignals.Returns(new Subject<OnlineTimeoutSignal>());
            networkBridge.BindAsync(Arg.Any<string>(), Arg.Any<bool>()).Returns(UniTask.CompletedTask);

            _backHandler.ClearReceivedCalls();

            var sut = CreateStartup(
                Substitute.For<IMatchFailSafeGateway>(),
                networkBridge: networkBridge,
                onlineSessionContextStore: onlineSessionStore,
                matchStateProvider: matchStateProvider,
                onlineSessionFlow: onlineFlow,
                statisticsReporter: CreateStatisticsReporter(_configStore, _eventStream));

            await sut.StartAsync(CancellationToken.None);

            onlineFlow.Emit(OnlineFlowState.Terminated, OnlineErrorCode.OpponentLeft);
            await UniTask.DelayFrame(1);

            _seriesService.Received(1)
                .RecordResult(Arg.Is<GameResult>(result =>
                    result.Status == RulesGameStatus.Timeout &&
                    result.Winner == PlayerMark.O));

            _fieldContainer.ClassListContains("field-container--round-finished").Should().BeTrue();
            await _backHandler.DidNotReceive().HandleBackAsync(Arg.Any<CancellationToken>());

            sut.Dispose();
        }

        [Test]
        public async Task WhenIncomingOnlineTimeoutSignalOnGuest_ThenSubmitsTimeoutCommandToMatchStateProvider()
        {
            _ecsLifecycle.IsActive.Returns(true);

            var timeoutSignals = new Subject<OnlineTimeoutSignal>();
            var onlineSessionStore = new OnlineGameplaySessionContextStore();
            onlineSessionStore.SetDirectInviteSession("ABCDEF", "guest-user", isHost: false);

            var matchStateProvider = Substitute.For<IMatchStateProvider>();
            var networkBridge = Substitute.For<IGameplayNetworkBridge>();
            networkBridge.Snapshot.Returns(new ReactiveProperty<GameplayNetworkSnapshot?>(null));
            networkBridge.IncomingMoves.Returns(new Subject<MoveCommand>());
            networkBridge.IncomingRoundReadySignals.Returns(new Subject<RoundReadySignal>());
            networkBridge.IncomingTimeoutSignals.Returns(timeoutSignals);
            networkBridge.BindAsync(Arg.Any<string>(), Arg.Any<bool>()).Returns(UniTask.CompletedTask);

            var sut = CreateStartup(
                Substitute.For<IMatchFailSafeGateway>(),
                networkBridge: networkBridge,
                onlineSessionContextStore: onlineSessionStore,
                matchStateProvider: matchStateProvider,
                onlineSessionFlow: new TestOnlineSessionFlow(),
                statisticsReporter: CreateStatisticsReporter(_configStore, _eventStream));

            await sut.StartAsync(CancellationToken.None);

            timeoutSignals.OnNext(new OnlineTimeoutSignal("host-user", loserSlot: 1, clientTick: 123));
            await UniTask.DelayFrame(1);

            matchStateProvider.Received(1)
                .SubmitCommand(Arg.Is<IGameplayCommand>(command => command is TimeoutCommand && ((TimeoutCommand)command).LoserSlot == 1));

            sut.Dispose();
            timeoutSignals.Dispose();
        }

        [Test]
        public async Task WhenIncomingOnlineTimeoutSignalOnHost_ThenDoesNotSubmitTimeoutCommandToMatchStateProvider()
        {
            _ecsLifecycle.IsActive.Returns(true);

            var timeoutSignals = new Subject<OnlineTimeoutSignal>();
            var onlineSessionStore = new OnlineGameplaySessionContextStore();
            onlineSessionStore.SetDirectInviteSession("ABCDEF", "host-user", isHost: true);

            var matchStateProvider = Substitute.For<IMatchStateProvider>();
            var networkBridge = Substitute.For<IGameplayNetworkBridge>();
            networkBridge.Snapshot.Returns(new ReactiveProperty<GameplayNetworkSnapshot?>(null));
            networkBridge.IncomingMoves.Returns(new Subject<MoveCommand>());
            networkBridge.IncomingRoundReadySignals.Returns(new Subject<RoundReadySignal>());
            networkBridge.IncomingTimeoutSignals.Returns(timeoutSignals);
            networkBridge.BindAsync(Arg.Any<string>(), Arg.Any<bool>()).Returns(UniTask.CompletedTask);

            var sut = CreateStartup(
                Substitute.For<IMatchFailSafeGateway>(),
                networkBridge: networkBridge,
                onlineSessionContextStore: onlineSessionStore,
                matchStateProvider: matchStateProvider,
                onlineSessionFlow: new TestOnlineSessionFlow(),
                statisticsReporter: CreateStatisticsReporter(_configStore, _eventStream));

            await sut.StartAsync(CancellationToken.None);

            timeoutSignals.OnNext(new OnlineTimeoutSignal("host-user", loserSlot: 1, clientTick: 123));
            await UniTask.DelayFrame(1);

            matchStateProvider.DidNotReceive().SubmitCommand(Arg.Any<IGameplayCommand>());

            sut.Dispose();
            timeoutSignals.Dispose();
        }

        private sealed class TestOnlineSessionFlow : IOnlineSessionFlowService
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

            public ReadOnlyReactiveProperty<OnlineFlowSnapshot> Snapshot => _snapshot;

            public void Emit(OnlineFlowState state, OnlineErrorCode errorCode) =>
                _snapshot.Value = new OnlineFlowSnapshot(
                    state,
                    previousStableState: null,
                    candidateSessionId: string.Empty,
                    activeSessionId: "ABCDEF",
                    flowEpoch: _snapshot.Value.FlowEpoch + 1,
                    region: "eu",
                    canStart: false,
                    isBusy: false,
                    errorCode: errorCode,
                    errorLocalizationKey: OnlineLocalizationKeys.ErrorKey(errorCode),
                    statusLocalizationKey: null,
                    countdownRemainingSeconds: null,
                    graceDeadlineUtc: null);

            public UniTask EnterHumanSetupAsync(string region, string currentUserId) => UniTask.CompletedTask;
            public UniTask ConfirmHostIntentAsync() => UniTask.CompletedTask;
            public UniTask StartHostSessionAsync(OnlineSessionConfig hostConfig) => UniTask.CompletedTask;
            public UniTask JoinBySessionIdAsync(string rawSessionIdInput, string region, string currentUserId) => UniTask.CompletedTask;
            public UniTask CopyVisibleSessionIdAsync() => UniTask.CompletedTask;
            public UniTask BackAsync() => UniTask.CompletedTask;
            public UniTask ExitAsync() => UniTask.CompletedTask;
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