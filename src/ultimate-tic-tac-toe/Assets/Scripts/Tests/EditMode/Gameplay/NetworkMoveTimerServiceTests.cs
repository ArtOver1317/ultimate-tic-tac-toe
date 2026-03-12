#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using R3;
using Runtime.GameModes.Wizard;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.Modes;
using Runtime.GameModes.Wizard.Online;
using Runtime.Gameplay;
using Runtime.Gameplay.ECS;
using Runtime.Gameplay.Shared;

namespace Tests.EditMode.Gameplay
{
    [TestFixture]
    [Category("Unit")]
    public class NetworkMoveTimerServiceTests
    {
        private sealed class FakeTimeSource : ITimeSource
        {
            public float DeltaTime { get; set; }
        }

        private sealed class FakeGameplayEventStream : IGameplayEventStream
        {
            private readonly Subject<CellChangedEvent> _cellChanged = new();
            private readonly Subject<LastMoveChangedEvent> _lastMoveChanged = new();
            private readonly Subject<CurrentPlayerChangedEvent> _currentPlayerChanged = new();
            private readonly Subject<CommandRejectedEvent> _commandRejected = new();
            private readonly Subject<RoundFinishedEvent> _roundFinished = new();

            public Observable<CellChangedEvent> CellChanged => _cellChanged;
            public Observable<LastMoveChangedEvent> LastMoveChanged => _lastMoveChanged;
            public Observable<CurrentPlayerChangedEvent> CurrentPlayerChanged => _currentPlayerChanged;
            public Observable<CommandRejectedEvent> CommandRejected => _commandRejected;
            public Observable<RoundFinishedEvent> RoundFinished => _roundFinished;

            public void PublishCurrentPlayerChanged(int slot) => _currentPlayerChanged.OnNext(new CurrentPlayerChangedEvent(slot));
            public void PublishRoundFinished(GameStatus status) => _roundFinished.OnNext(new RoundFinishedEvent(status, null, null));
        }

        private sealed class CapturingCommandSink : IGameplayCommandSink
        {
            public List<IGameplayCommand> Commands { get; } = new();
            public void SubmitCommand(IGameplayCommand command) => Commands.Add(command);
        }

        private sealed class FakeOnlineSessionContextStore : IOnlineGameplaySessionContextStore
        {
            private OnlineGameplaySessionSnapshot _snapshot;

            public FakeOnlineSessionContextStore(bool isHost, OnlineMatchConfigPayload? matchConfig = null) =>
                _snapshot = new OnlineGameplaySessionSnapshot(
                    isOnlineDirectInvite: true,
                    sessionId: "ABCDEF",
                    localUserId: "user-local",
                    isHost: isHost,
                    matchConfig: matchConfig);

            public OnlineGameplaySessionSnapshot Snapshot => _snapshot;
            public void SetOnlineSession(string sessionId, string localUserId, bool isHost) => throw new NotSupportedException();
            public void SetDirectInviteSession(string sessionId, string localUserId, bool isHost) => throw new NotSupportedException();
            public void SetMatchConfig(OnlineMatchConfigPayload matchConfig) => throw new NotSupportedException();
            public void Clear() => _snapshot = OnlineGameplaySessionSnapshot.Empty();
        }

        private static GameLaunchConfigStore CreateStoreWithLimit(int seconds)
        {
            var store = new GameLaunchConfigStore();
            store.Set(new GameLaunchConfig("classic", new TicTacToeConfig(3), new LocalHumanConfig(), seconds));
            return store;
        }

        [Test]
        public async Task WhenHostTimerExpires_ThenSubmitsTimeoutCommand()
        {
            var stream = new FakeGameplayEventStream();
            var sink = new CapturingCommandSink();
            var time = new FakeTimeSource { DeltaTime = 0.6f };
            var context = new FakeOnlineSessionContextStore(isHost: true);

            using var sut = new NetworkMoveTimerService(CreateStoreWithLimit(1), stream, sink, time, context);
            sut.StartOrResetForPlayer(1);

            await WaitUntilAsync(() => sink.Commands.Count == 1, maxFrames: 240);

            sink.Commands.Should().ContainSingle();
            sink.Commands[0].Should().BeOfType<TimeoutCommand>();
            ((TimeoutCommand)sink.Commands[0]).LoserSlot.Should().Be(1);
        }

        [Test]
        public async Task WhenClientTimerExpires_ThenDoesNotSubmitTimeoutCommand()
        {
            var stream = new FakeGameplayEventStream();
            var sink = new CapturingCommandSink();
            var time = new FakeTimeSource { DeltaTime = 1.0f };
            var context = new FakeOnlineSessionContextStore(isHost: false);

            using var sut = new NetworkMoveTimerService(CreateStoreWithLimit(1), stream, sink, time, context);
            sut.StartOrResetForPlayer(0);

            await WaitUntilAsync(() => sut.IsActive.CurrentValue == false, maxFrames: 240);

            sink.Commands.Should().BeEmpty();
            sut.IsActive.CurrentValue.Should().BeFalse();
            sut.RemainingSeconds.CurrentValue.Should().Be(0f);
        }

        [Test]
        public async Task WhenRoundFinishedEventReceived_ThenNetworkTimerStops()
        {
            var stream = new FakeGameplayEventStream();
            var sink = new CapturingCommandSink();
            var time = new FakeTimeSource { DeltaTime = 0f };
            var context = new FakeOnlineSessionContextStore(isHost: true);

            using var sut = new NetworkMoveTimerService(CreateStoreWithLimit(5), stream, sink, time, context);
            sut.StartOrResetForPlayer(0);

            stream.PublishRoundFinished(GameStatus.Win);
            await UniTask.DelayFrame(1);

            sut.IsActive.CurrentValue.Should().BeFalse();
        }

        [Test]
        public async Task WhenCurrentPlayerChangedEventReceived_ThenNetworkTimerResets()
        {
            var stream = new FakeGameplayEventStream();
            var sink = new CapturingCommandSink();
            var time = new FakeTimeSource { DeltaTime = 0f };
            var context = new FakeOnlineSessionContextStore(isHost: true);

            using var sut = new NetworkMoveTimerService(CreateStoreWithLimit(5), stream, sink, time, context);
            sut.StartOrResetForPlayer(0);
            stream.PublishCurrentPlayerChanged(1);

            sut.RemainingSeconds.CurrentValue.Should().Be(5f);
            sut.IsActive.CurrentValue.Should().BeTrue();

            stream.PublishRoundFinished(GameStatus.Win);
            await WaitUntilAsync(() => sut.IsActive.CurrentValue == false, maxFrames: 5);
            sut.IsActive.CurrentValue.Should().BeFalse();
        }

        [Test]
        public void WhenOnlineMatchConfigHasMoveTimer_ThenUsesPayloadLimitInsteadOfStoreLimit()
        {
            var stream = new FakeGameplayEventStream();
            var sink = new CapturingCommandSink();
            var time = new FakeTimeSource { DeltaTime = 0f };
            var context = new FakeOnlineSessionContextStore(
                isHost: true,
                matchConfig: new OnlineMatchConfigPayload("classic", boardSize: 3, isUltimate: false, moveTimeLimitSeconds: 12));

            using var sut = new NetworkMoveTimerService(CreateStoreWithLimit(3), stream, sink, time, context);
            sut.StartOrResetForPlayer(0);

            sut.RemainingSeconds.CurrentValue.Should().Be(12f);
            sut.IsActive.CurrentValue.Should().BeTrue();
        }

        [Test]
        public async Task WhenMoveTimeLimitIsZero_ThenTimerDoesNotActivateAndNoTimeoutSubmitted()
        {
            var stream = new FakeGameplayEventStream();
            var sink = new CapturingCommandSink();
            var time = new FakeTimeSource { DeltaTime = 1f };
            var context = new FakeOnlineSessionContextStore(isHost: true);

            using var sut = new NetworkMoveTimerService(CreateStoreWithLimit(0), stream, sink, time, context);
            sut.StartOrResetForPlayer(0);

            await UniTask.DelayFrame(3);

            sut.IsActive.CurrentValue.Should().BeFalse();
            sink.Commands.Should().BeEmpty();
        }

        private static async UniTask WaitUntilAsync(Func<bool> predicate, int maxFrames)
        {
            for (var i = 0; i < maxFrames; i++)
            {
                if (predicate())
                    return;

                await UniTask.DelayFrame(1);
            }

            predicate().Should().BeTrue("condition should become true within allotted frames");
        }
    }
}
