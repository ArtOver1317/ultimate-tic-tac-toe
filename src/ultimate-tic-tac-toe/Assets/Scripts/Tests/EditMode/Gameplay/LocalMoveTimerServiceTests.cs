#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using R3;
using Runtime.GameModes.Wizard;
using Runtime.Gameplay;
using Runtime.Gameplay.ECS;
using CellId = Runtime.Games.TicTacToe.Moves.CellId;

namespace Tests.EditMode.Gameplay
{
    [TestFixture]
    [Category("Unit")]
    public class LocalMoveTimerServiceTests
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

        private static GameLaunchConfigStore CreateStoreWithLimit(int seconds)
        {
            var store = new GameLaunchConfigStore();
            store.Set(new GameLaunchConfig("classic", new TicTacToeConfig(3), new LocalHumanConfig(), seconds));
            return store;
        }

        [Test]
        public async Task WhenTimerExpires_ThenSubmitsTimeoutCommand()
        {
            var stream = new FakeGameplayEventStream();
            var sink = new CapturingCommandSink();
            var time = new FakeTimeSource { DeltaTime = 0.6f };

            using var sut = new LocalMoveTimerService(CreateStoreWithLimit(1), stream, sink, time);
            sut.StartOrResetForPlayer(0);

            await UniTask.DelayFrame(3);

            sink.Commands.Should().ContainSingle();
            sink.Commands[0].Should().BeOfType<TimeoutCommand>();
            ((TimeoutCommand)sink.Commands[0]).LoserSlot.Should().Be(0);
            sut.IsActive.CurrentValue.Should().BeFalse();
        }

        [Test]
        public async Task WhenStartOrResetCalledAgain_ThenPreviousCountdownCancelled()
        {
            var stream = new FakeGameplayEventStream();
            var sink = new CapturingCommandSink();
            var time = new FakeTimeSource { DeltaTime = 0f };

            using var sut = new LocalMoveTimerService(CreateStoreWithLimit(5), stream, sink, time);
            sut.StartOrResetForPlayer(0);
            sut.StartOrResetForPlayer(1);

            await UniTask.DelayFrame(2);

            sink.Commands.Should().BeEmpty();
            sut.IsActive.CurrentValue.Should().BeTrue();
            sut.RemainingSeconds.CurrentValue.Should().Be(5f);
        }

        [Test]
        public async Task WhenMoveTimeLimitIsZero_ThenStartOrResetIsNoOp()
        {
            var stream = new FakeGameplayEventStream();
            var sink = new CapturingCommandSink();
            var time = new FakeTimeSource { DeltaTime = 1f };

            using var sut = new LocalMoveTimerService(CreateStoreWithLimit(0), stream, sink, time);
            sut.StartOrResetForPlayer(0);

            await UniTask.DelayFrame(2);

            sink.Commands.Should().BeEmpty();
            sut.IsActive.CurrentValue.Should().BeFalse();
            sut.RemainingSeconds.CurrentValue.Should().Be(0f);
        }

        [Test]
        public async Task WhenFreezeAndUnfreeze_ThenCountdownPausedAndResumed()
        {
            var stream = new FakeGameplayEventStream();
            var sink = new CapturingCommandSink();
            var time = new FakeTimeSource { DeltaTime = 1f };

            using var sut = new LocalMoveTimerService(CreateStoreWithLimit(2), stream, sink, time);
            sut.StartOrResetForPlayer(0);

            await UniTask.DelayFrame(1);
            var beforeFreeze = sut.RemainingSeconds.CurrentValue;

            sut.Freeze();
            await UniTask.DelayFrame(2);
            sut.RemainingSeconds.CurrentValue.Should().Be(beforeFreeze);
            sink.Commands.Should().BeEmpty();

            sut.Unfreeze();
            await UniTask.DelayFrame(1);

            sink.Commands.Should().ContainSingle(c => c is TimeoutCommand);
        }

        [Test]
        public void WhenStopCalledTwice_ThenNoExceptionThrown()
        {
            var stream = new FakeGameplayEventStream();
            var sink = new CapturingCommandSink();
            var time = new FakeTimeSource();

            using var sut = new LocalMoveTimerService(CreateStoreWithLimit(10), stream, sink, time);

            Action act = () =>
            {
                sut.Stop();
                sut.Stop();
            };

            act.Should().NotThrow();
            sut.IsActive.CurrentValue.Should().BeFalse();
        }
    }
}
