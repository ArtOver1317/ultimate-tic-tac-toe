#nullable enable

using System;
using System.Linq;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using R3;
using Runtime.GameModes.Wizard;
using Runtime.Gameplay;
using Runtime.Gameplay.ECS;
using Runtime.Games.Battleship;
using CellId = Runtime.Games.TicTacToe.Moves.CellId;

namespace Tests.EditMode.Games.Battleship
{
    [TestFixture]
    [Category("Unit")]
    public sealed class BattleshipPlacementTimerServiceTests
    {
        private sealed class FakeTimeSource : ITimeSource
        {
            public float DeltaTime { get; set; }
        }

        private sealed class CapturingCommandSink : IGameplayCommandSink
        {
            public readonly System.Collections.Generic.List<IGameplayCommand> Commands = new();
            public void SubmitCommand(IGameplayCommand command) => Commands.Add(command);
        }

        private sealed class FakeBattleshipSnapshotProvider : IBattleshipGameplaySnapshotProvider
        {
            public BattleshipPhase Phase { get; set; } = BattleshipPhase.Placement;
            public int ActivePlayerSlot { get; set; } = -1;
            public GameStatus CurrentStatus { get; set; } = GameStatus.InProgress;
            public int? WinnerSlot { get; set; }
            public bool Slot0Confirmed { get; set; }
            public bool Slot1Confirmed { get; set; }

            public bool IsPlacementConfirmed(int playerSlot) =>
                playerSlot == PlayerSlotMapping.SlotX
                    ? Slot0Confirmed
                    : playerSlot == PlayerSlotMapping.SlotO && Slot1Confirmed;

            public bool TryGetFleetLayout(int playerSlot, out FleetLayout layout)
            {
                layout = default;
                return false;
            }

            public bool TryGetConsecutiveTimeouts(out int player0ConsecutiveTimeouts, out int player1ConsecutiveTimeouts)
            {
                player0ConsecutiveTimeouts = 0;
                player1ConsecutiveTimeouts = 0;
                return true;
            }

            public System.Collections.Generic.IReadOnlyList<BattleshipCellMark> GetOpponentMarks(int viewerSlot) =>
                Array.Empty<BattleshipCellMark>();

            public System.Collections.Generic.IReadOnlyList<BattleshipCellMark> GetOwnMarks(int viewerSlot) =>
                Array.Empty<BattleshipCellMark>();
        }

        private sealed class FakeBattleshipEventStream : IBattleshipGameplayEventStream
        {
            private readonly Subject<BattleshipPhaseChangedEvent> _phaseChanged = new();
            private readonly Subject<BattleshipMarksChangedEvent> _marksChanged = new();

            public Observable<BattleshipPhaseChangedEvent> PhaseChanged => _phaseChanged;
            public Observable<BattleshipMarksChangedEvent> MarksChanged => _marksChanged;

            public void PublishPhase(BattleshipPhase phase) => _phaseChanged.OnNext(new BattleshipPhaseChangedEvent(phase));
        }

        private sealed class FakeMatchStateProvider : IMatchStateProvider
        {
            public bool IsMatchActive { get; set; } = true;

            public Observable<CellChangedEvent> CellChanged => Observable.Empty<CellChangedEvent>();
            public Observable<LastMoveChangedEvent> LastMoveChanged => Observable.Empty<LastMoveChangedEvent>();
            public Observable<CurrentPlayerChangedEvent> CurrentPlayerChanged => Observable.Empty<CurrentPlayerChangedEvent>();
            public Observable<CommandRejectedEvent> CommandRejected => Observable.Empty<CommandRejectedEvent>();
            public Observable<RoundFinishedEvent> RoundFinished => Observable.Empty<RoundFinishedEvent>();
            public int ActivePlayerSlot => 0;
            public CellId? LastMove => null;
            public long CommandSequence => 0;
            public int GetCellSlot(CellId cellId) => -1;
            public System.Collections.Generic.IReadOnlyList<CellSnapshot> GetAllCells() => Array.Empty<CellSnapshot>();
            public void SubmitCommand(IGameplayCommand command) { }
            public void Dispose() { }
        }

        private static GameLaunchConfigStore CreateStoreWithPlacementLimit(int placementSeconds)
        {
            var store = new GameLaunchConfigStore();
            store.Set(new GameLaunchConfig(
                BattleshipStrategy.DefaultGameId,
                new BattleshipConfig(placementSeconds),
                new BotOpponentConfig(BattleshipStrategy.DefaultBotDifficultyId),
                moveTimeLimitSeconds: 60));
            return store;
        }

        [Test]
        public async Task WhenTimerExpiresAndBothPlayersNotConfirmed_ThenSubmitsPlacementTimeoutForBothSlots()
        {
            var stream = new FakeBattleshipEventStream();
            var snapshot = new FakeBattleshipSnapshotProvider
            {
                Phase = BattleshipPhase.Placement,
                Slot0Confirmed = false,
                Slot1Confirmed = false,
            };
            var matchState = new FakeMatchStateProvider { IsMatchActive = true };
            var sink = new CapturingCommandSink();
            var time = new FakeTimeSource { DeltaTime = 0.55f };
            var sessionStore = new OnlineGameplaySessionContextStore();

            using var sut = new BattleshipPlacementTimerService(
                CreateStoreWithPlacementLimit(1),
                stream,
                snapshot,
                matchState,
                sink,
                time,
                sessionStore);

            sut.SyncFromSnapshot();
            await WaitUntilAsync(() => sink.Commands.OfType<PlacementTimeoutCommand>().Count() == 2);

            var timeoutCommands = sink.Commands.OfType<PlacementTimeoutCommand>().ToArray();
            timeoutCommands.Should().HaveCount(2);
            timeoutCommands.Should().Contain(command => command.PlayerSlot == PlayerSlotMapping.SlotX);
            timeoutCommands.Should().Contain(command => command.PlayerSlot == PlayerSlotMapping.SlotO);
        }

        [Test]
        public async Task WhenGuestOnlineAndTimerExpires_ThenDoesNotSubmitPlacementTimeoutCommands()
        {
            var stream = new FakeBattleshipEventStream();
            var snapshot = new FakeBattleshipSnapshotProvider
            {
                Phase = BattleshipPhase.Waiting,
                Slot0Confirmed = true,
                Slot1Confirmed = false,
            };
            var matchState = new FakeMatchStateProvider { IsMatchActive = true };
            var sink = new CapturingCommandSink();
            var time = new FakeTimeSource { DeltaTime = 0.6f };
            var sessionStore = new OnlineGameplaySessionContextStore();
            sessionStore.SetDirectInviteSession("ABCDEF", "guest-user", isHost: false);

            using var sut = new BattleshipPlacementTimerService(
                CreateStoreWithPlacementLimit(1),
                stream,
                snapshot,
                matchState,
                sink,
                time,
                sessionStore);

            sut.SyncFromSnapshot();
            await WaitUntilAsync(() => sut.RemainingSeconds.CurrentValue <= 0f);

            sink.Commands.Should().BeEmpty();
            sut.IsActive.CurrentValue.Should().BeTrue();
        }

        [Test]
        public async Task WhenOnePlayerAlreadyConfirmed_ThenOnlyNonConfirmedPlayerGetTimeout()
        {
            var stream = new FakeBattleshipEventStream();
            var snapshot = new FakeBattleshipSnapshotProvider
            {
                Phase = BattleshipPhase.Waiting,
                Slot0Confirmed = true,
                Slot1Confirmed = false,
            };
            var matchState = new FakeMatchStateProvider { IsMatchActive = true };
            var sink = new CapturingCommandSink();
            var time = new FakeTimeSource { DeltaTime = 0.55f };
            var sessionStore = new OnlineGameplaySessionContextStore();

            using var sut = new BattleshipPlacementTimerService(
                CreateStoreWithPlacementLimit(1),
                stream,
                snapshot,
                matchState,
                sink,
                time,
                sessionStore);

            sut.SyncFromSnapshot();
            await WaitUntilAsync(() => sink.Commands.OfType<PlacementTimeoutCommand>().Any());

            var timeoutCommands = sink.Commands.OfType<PlacementTimeoutCommand>().ToArray();
            timeoutCommands.Should().ContainSingle();
            timeoutCommands[0].PlayerSlot.Should().Be(PlayerSlotMapping.SlotO);
        }

        [Test]
        public async Task WhenPhaseChangesToBattle_ThenTimerStopsAndNoMoreTimeoutsSubmitted()
        {
            var stream = new FakeBattleshipEventStream();
            var snapshot = new FakeBattleshipSnapshotProvider
            {
                Phase = BattleshipPhase.Placement,
                Slot0Confirmed = false,
                Slot1Confirmed = false,
            };
            var matchState = new FakeMatchStateProvider { IsMatchActive = true };
            var sink = new CapturingCommandSink();
            var time = new FakeTimeSource { DeltaTime = 0.6f };
            var sessionStore = new OnlineGameplaySessionContextStore();

            using var sut = new BattleshipPlacementTimerService(
                CreateStoreWithPlacementLimit(1),
                stream,
                snapshot,
                matchState,
                sink,
                time,
                sessionStore);

            sut.SyncFromSnapshot();
            snapshot.Phase = BattleshipPhase.Battle;
            stream.PublishPhase(BattleshipPhase.Battle);

            await WaitUntilAsync(() => !sut.IsActive.CurrentValue);

            sink.Commands.Should().BeEmpty();
            sut.IsActive.CurrentValue.Should().BeFalse();
        }

        private static async Task WaitUntilAsync(Func<bool> condition, int maxFrames = 20)
        {
            for (var frame = 0; frame < maxFrames; frame++)
            {
                if (condition())
                    return;

                await UniTask.Yield();
            }

            Assert.Fail("Expected condition to become true within the allotted frames.");
        }
    }
}
