#nullable enable

using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using R3;
using Runtime.GameModes.Wizard;
using Runtime.GameModes.Wizard.Online;
using Runtime.Gameplay;
using Runtime.Gameplay.ECS;
using Runtime.Gameplay.Shared;
using Runtime.Games.TicTacToe.Moves;

namespace Tests.EditMode.Gameplay
{
    [TestFixture]
    [Category("Unit")]
    public class OnlineAwareGameplayCommandSinkTests
    {
        [Test]
        public void WhenOnlineGuestAttemptsMoveOnHostTurn_ThenIgnoresLocalAndNetworkMove()
        {
            // Arrange
            var localSink = Substitute.For<IMatchStateProvider>();
            localSink.ActivePlayerSlot.Returns(0); // host turn

            var snapshotProvider = Substitute.For<IGameplaySnapshotProvider>();
            snapshotProvider.GetAllCells().Returns(Array.Empty<CellSnapshot>());

            var bridge = new SpyGameplayNetworkBridge();
            var contextStore = new OnlineGameplaySessionContextStore();
            contextStore.SetDirectInviteSession("AB2CD7", "guest-user", isHost: false);

            var sut = new OnlineAwareGameplayCommandSink(localSink, snapshotProvider, bridge, contextStore);

            // Act
            sut.SubmitCommand(new MakeMoveCommand(new CellId(0, 0)));

            // Assert
            localSink.DidNotReceive().SubmitCommand(Arg.Any<IGameplayCommand>());
            bridge.SubmitMoveCalls.Should().Be(0);
        }

        [Test]
        public void WhenOnlineHostMovesOnHostTurn_ThenSubmitsLocalAndNetworkMove()
        {
            // Arrange
            var localSink = Substitute.For<IMatchStateProvider>();
            localSink.ActivePlayerSlot.Returns(0); // host turn

            var snapshotProvider = Substitute.For<IGameplaySnapshotProvider>();
            snapshotProvider.GetAllCells().Returns(Array.Empty<CellSnapshot>());

            var bridge = new SpyGameplayNetworkBridge();
            var contextStore = new OnlineGameplaySessionContextStore();
            contextStore.SetDirectInviteSession("AB2CD7", "host-user", isHost: true);

            var sut = new OnlineAwareGameplayCommandSink(localSink, snapshotProvider, bridge, contextStore);

            // Act
            sut.SubmitCommand(new MakeMoveCommand(new CellId(0, 0)));

            // Assert
            localSink.Received(1).SubmitCommand(Arg.Any<IGameplayCommand>());
            bridge.SubmitMoveCalls.Should().Be(1);
        }

        [Test]
        public void WhenOnlineGuestMovesOnGuestTurn_ThenSubmitsNetworkMoveWithoutLocalApply()
        {
            // Arrange
            var localSink = Substitute.For<IMatchStateProvider>();
            localSink.ActivePlayerSlot.Returns(1); // guest turn

            var snapshotProvider = Substitute.For<IGameplaySnapshotProvider>();
            snapshotProvider.GetAllCells().Returns(Array.Empty<CellSnapshot>());

            var bridge = new SpyGameplayNetworkBridge();
            var contextStore = new OnlineGameplaySessionContextStore();
            contextStore.SetDirectInviteSession("AB2CD7", "guest-user", isHost: false);

            var sut = new OnlineAwareGameplayCommandSink(localSink, snapshotProvider, bridge, contextStore);

            // Act
            sut.SubmitCommand(new MakeMoveCommand(new CellId(0, 0)));

            // Assert
            localSink.DidNotReceive().SubmitCommand(Arg.Any<IGameplayCommand>());
            bridge.SubmitMoveCalls.Should().Be(1);
        }

        [Test]
        public void WhenOfflineMoveSubmitted_ThenSubmitsLocally()
        {
            // Arrange
            var localSink = Substitute.For<IMatchStateProvider>();
            var snapshotProvider = Substitute.For<IGameplaySnapshotProvider>();
            var bridge = new SpyGameplayNetworkBridge();
            var contextStore = new OnlineGameplaySessionContextStore();

            var sut = new OnlineAwareGameplayCommandSink(localSink, snapshotProvider, bridge, contextStore);

            // Act
            sut.SubmitCommand(new MakeMoveCommand(new CellId(0, 0)));

            // Assert
            localSink.Received(1).SubmitCommand(Arg.Any<IGameplayCommand>());
            bridge.SubmitMoveCalls.Should().Be(0);
        }

        [Test]
        public void WhenOnlineHostSubmitsTimeout_ThenSubmitsLocalAndNetworkTimeout()
        {
            var localSink = Substitute.For<IMatchStateProvider>();
            var snapshotProvider = Substitute.For<IGameplaySnapshotProvider>();
            var bridge = new SpyGameplayNetworkBridge();
            var contextStore = new OnlineGameplaySessionContextStore();
            contextStore.SetDirectInviteSession("AB2CD7", "host-user", isHost: true);

            var sut = new OnlineAwareGameplayCommandSink(localSink, snapshotProvider, bridge, contextStore);

            sut.SubmitCommand(new TimeoutCommand(loserSlot: 1));

            localSink.Received(1).SubmitCommand(Arg.Any<IGameplayCommand>());
            bridge.SubmitTimeoutCalls.Should().Be(1);
        }

        [Test]
        public void WhenOnlineGuestSubmitsTimeout_ThenDoesNotSubmitLocalAndNetworkTimeout()
        {
            var localSink = Substitute.For<IMatchStateProvider>();
            var snapshotProvider = Substitute.For<IGameplaySnapshotProvider>();
            var bridge = new SpyGameplayNetworkBridge();
            var contextStore = new OnlineGameplaySessionContextStore();
            contextStore.SetDirectInviteSession("AB2CD7", "guest-user", isHost: false);

            var sut = new OnlineAwareGameplayCommandSink(localSink, snapshotProvider, bridge, contextStore);

            sut.SubmitCommand(new TimeoutCommand(loserSlot: 0));

            localSink.DidNotReceive().SubmitCommand(Arg.Any<IGameplayCommand>());
            bridge.SubmitTimeoutCalls.Should().Be(0);
        }

        private sealed class SpyGameplayNetworkBridge : IGameplayNetworkBridge
        {
            private readonly ReactiveProperty<GameplayNetworkSnapshot?> _snapshot = new(null);
            private readonly Subject<MoveCommand> _incomingMoves = new();
            private readonly Subject<RoundReadySignal> _incomingRoundReadySignals = new();
            private readonly Subject<OnlineTimeoutSignal> _incomingTimeoutSignals = new();

            public int SubmitMoveCalls { get; private set; }
            public int SubmitTimeoutCalls { get; private set; }

            public ReadOnlyReactiveProperty<GameplayNetworkSnapshot?> Snapshot => _snapshot;
            public Observable<MoveCommand> IncomingMoves => _incomingMoves;
            public Observable<RoundReadySignal> IncomingRoundReadySignals => _incomingRoundReadySignals;
            public Observable<OnlineTimeoutSignal> IncomingTimeoutSignals => _incomingTimeoutSignals;

            public UniTask BindAsync(string localUserId, bool isHost) => UniTask.CompletedTask;
            public UniTask UnbindAsync() => UniTask.CompletedTask;

            public UniTask SubmitMoveAsync(MoveCommand command)
            {
                SubmitMoveCalls++;
                return UniTask.CompletedTask;
            }

            public UniTask SubmitRoundReadyAsync(RoundReadySignal signal) => UniTask.CompletedTask;

            public UniTask SubmitTimeoutAsync(OnlineTimeoutSignal signal)
            {
                SubmitTimeoutCalls++;
                return UniTask.CompletedTask;
            }

            public void Dispose()
            {
                _snapshot.Dispose();
                _incomingMoves.Dispose();
                _incomingRoundReadySignals.Dispose();
                _incomingTimeoutSignals.Dispose();
            }
        }
    }
}

#nullable restore
