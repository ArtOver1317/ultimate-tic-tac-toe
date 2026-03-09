#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
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
using Runtime.Games.Battleship;
using Runtime.Games.TicTacToe.AI;
using Runtime.Games.TicTacToe.Moves;

namespace Tests.EditMode.Games.Battleship
{
    [TestFixture]
    [Category("Unit")]
    public sealed class BattleshipBotDriverTests
    {
        private sealed class FakeBattleshipSnapshotProvider : IBattleshipGameplaySnapshotProvider
        {
            public BattleshipPhase Phase { get; set; } = BattleshipPhase.Placement;
            public int ActivePlayerSlot { get; set; } = PlayerSlotMapping.SlotX;
            public GameStatus CurrentStatus { get; set; } = GameStatus.InProgress;
            public int? WinnerSlot { get; set; }
            public bool SlotXConfirmed { get; set; }
            public bool SlotOConfirmed { get; set; }
            public BattleshipCellMark[] OpponentMarksForO { get; set; } = CreateUnknownMarks();

            public bool IsPlacementConfirmed(int playerSlot) =>
                playerSlot == PlayerSlotMapping.SlotX
                    ? SlotXConfirmed
                    : playerSlot == PlayerSlotMapping.SlotO && SlotOConfirmed;

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

            public IReadOnlyList<BattleshipCellMark> GetOpponentMarks(int viewerSlot) =>
                viewerSlot == PlayerSlotMapping.SlotO
                    ? OpponentMarksForO
                    : Array.Empty<BattleshipCellMark>();

            public IReadOnlyList<BattleshipCellMark> GetOwnMarks(int viewerSlot) => Array.Empty<BattleshipCellMark>();

            private static BattleshipCellMark[] CreateUnknownMarks()
            {
                var marks = new BattleshipCellMark[100];
                for (var i = 0; i < marks.Length; i++)
                    marks[i] = BattleshipCellMark.Unknown;
                return marks;
            }
        }

        private sealed class FakeBattleshipEventStream : IBattleshipGameplayEventStream
        {
            private readonly Subject<BattleshipPhaseChangedEvent> _phase = new();
            private readonly Subject<BattleshipMarksChangedEvent> _marks = new();

            public Observable<BattleshipPhaseChangedEvent> PhaseChanged => _phase;
            public Observable<BattleshipMarksChangedEvent> MarksChanged => _marks;

            public void PublishPhase(BattleshipPhase phase) => _phase.OnNext(new BattleshipPhaseChangedEvent(phase));
        }

        private sealed class FakeGameplayEventStream : IGameplayEventStream
        {
            public Subject<CellChangedEvent> CellChangedSource { get; } = new();
            public Subject<LastMoveChangedEvent> LastMoveChangedSource { get; } = new();
            public Subject<CurrentPlayerChangedEvent> CurrentPlayerChangedSource { get; } = new();
            public Subject<CommandRejectedEvent> CommandRejectedSource { get; } = new();
            public Subject<RoundFinishedEvent> RoundFinishedSource { get; } = new();

            public Observable<CellChangedEvent> CellChanged => CellChangedSource;
            public Observable<LastMoveChangedEvent> LastMoveChanged => LastMoveChangedSource;
            public Observable<CurrentPlayerChangedEvent> CurrentPlayerChanged => CurrentPlayerChangedSource;
            public Observable<CommandRejectedEvent> CommandRejected => CommandRejectedSource;
            public Observable<RoundFinishedEvent> RoundFinished => RoundFinishedSource;
        }

        private sealed class CapturingCommandSink : IGameplayCommandSink
        {
            private readonly FakeBattleshipSnapshotProvider _snapshot;

            public List<IGameplayCommand> Commands { get; } = new();

            public CapturingCommandSink(FakeBattleshipSnapshotProvider snapshot) => _snapshot = snapshot;

            public void SubmitCommand(IGameplayCommand command)
            {
                Commands.Add(command);

                if (command is SubmitPlacementCommand submitPlacement
                    && submitPlacement.PlayerSlot == PlayerSlotMapping.SlotO)
                {
                    _snapshot.SlotOConfirmed = true;
                }

                if (command is MakeMoveCommand)
                {
                    _snapshot.ActivePlayerSlot = PlayerSlotMapping.SlotX;
                }
            }
        }

        private sealed class PassiveCapturingCommandSink : IGameplayCommandSink
        {
            public List<IGameplayCommand> Commands { get; } = new();

            public void SubmitCommand(IGameplayCommand command)
            {
                Commands.Add(command);
            }
        }

        private sealed class CyclingCommandSink : IGameplayCommandSink
        {
            private readonly FakeBattleshipSnapshotProvider _snapshot;

            public CyclingCommandSink(FakeBattleshipSnapshotProvider snapshot) => _snapshot = snapshot;

            public List<IGameplayCommand> Commands { get; } = new();

            public void SubmitCommand(IGameplayCommand command)
            {
                Commands.Add(command);

                if (command is not MakeMoveCommand move)
                    return;

                var index = (move.CellId.Major * 10) + move.CellId.Minor;
                _snapshot.OpponentMarksForO[index] = BattleshipCellMark.Miss;
                _snapshot.ActivePlayerSlot = PlayerSlotMapping.SlotO;
            }
        }

        [Test]
        public async Task WhenStartInPlacementPhase_ThenBotSubmitsPlacementCommand()
        {
            var snapshot = new FakeBattleshipSnapshotProvider
            {
                Phase = BattleshipPhase.Placement,
                ActivePlayerSlot = PlayerSlotMapping.SlotX,
            };
            var battleshipEvents = new FakeBattleshipEventStream();
            var gameplayEvents = new FakeGameplayEventStream();
            var sink = new CapturingCommandSink(snapshot);
            var sessionStore = new OnlineGameplaySessionContextStore();
            var autoPlacer = new BattleshipAutoPlacer(new BattleshipPlacementValidator());

            using var sut = new BattleshipBotDriver(
                snapshot,
                battleshipEvents,
                gameplayEvents,
                sink,
                autoPlacer,
                sessionStore,
                CreateSettings(botShotDelaySeconds: 0f));

            var config = new GameLaunchConfig(
                BattleshipStrategy.DefaultGameId,
                new BattleshipConfig(placementTimeLimitSeconds: 30),
                new BotOpponentConfig(BattleshipStrategy.DefaultBotDifficultyId),
                moveTimeLimitSeconds: 30);

            var result = await sut.StartAsync(config, PlayerSlotMapping.SlotO, CancellationToken.None);

            result.Status.Should().Be(BotStartStatus.Started);
            sink.Commands.Should().ContainSingle(command => command is SubmitPlacementCommand);
        }

        [Test]
        public async Task WhenBotTurnInBattlePhase_ThenBotSubmitsMoveCommand()
        {
            var snapshot = new FakeBattleshipSnapshotProvider
            {
                Phase = BattleshipPhase.Battle,
                ActivePlayerSlot = PlayerSlotMapping.SlotO,
                SlotXConfirmed = true,
                SlotOConfirmed = true,
            };
            var battleshipEvents = new FakeBattleshipEventStream();
            var gameplayEvents = new FakeGameplayEventStream();
            var sink = new CapturingCommandSink(snapshot);
            var sessionStore = new OnlineGameplaySessionContextStore();
            var autoPlacer = new BattleshipAutoPlacer(new BattleshipPlacementValidator());

            using var sut = new BattleshipBotDriver(
                snapshot,
                battleshipEvents,
                gameplayEvents,
                sink,
                autoPlacer,
                sessionStore,
                CreateSettings(botShotDelaySeconds: 0f));

            var config = new GameLaunchConfig(
                BattleshipStrategy.DefaultGameId,
                new BattleshipConfig(placementTimeLimitSeconds: 30),
                new BotOpponentConfig(BattleshipStrategy.DefaultBotDifficultyId),
                moveTimeLimitSeconds: 30);

            var result = await sut.StartAsync(config, PlayerSlotMapping.SlotO, CancellationToken.None);
            result.Status.Should().Be(BotStartStatus.Started);

            await WaitUntilAsync(() => sink.Commands.Exists(command => command is MakeMoveCommand));

            sink.Commands.Should().Contain(command => command is MakeMoveCommand);
        }

        [Test]
        public async Task WhenOpponentHasDamagedShip_ThenBotTargetsAdjacentUnknownCellToFinish()
        {
            var snapshot = new FakeBattleshipSnapshotProvider
            {
                Phase = BattleshipPhase.Battle,
                ActivePlayerSlot = PlayerSlotMapping.SlotO,
                SlotXConfirmed = true,
                SlotOConfirmed = true,
                OpponentMarksForO = CreateMarksWithSingleFinishCandidate(),
            };

            var battleshipEvents = new FakeBattleshipEventStream();
            var gameplayEvents = new FakeGameplayEventStream();
            var sink = new CapturingCommandSink(snapshot);
            var sessionStore = new OnlineGameplaySessionContextStore();
            var autoPlacer = new BattleshipAutoPlacer(new BattleshipPlacementValidator());

            using var sut = new BattleshipBotDriver(
                snapshot,
                battleshipEvents,
                gameplayEvents,
                sink,
                autoPlacer,
                sessionStore,
                CreateSettings(botShotDelaySeconds: 0f));

            var config = new GameLaunchConfig(
                BattleshipStrategy.DefaultGameId,
                new BattleshipConfig(placementTimeLimitSeconds: 30),
                new BotOpponentConfig(BattleshipStrategy.DefaultBotDifficultyId),
                moveTimeLimitSeconds: 30);

            var result = await sut.StartAsync(config, PlayerSlotMapping.SlotO, CancellationToken.None);
            result.Status.Should().Be(BotStartStatus.Started);

            await WaitUntilAsync(() => sink.Commands.Exists(command => command is MakeMoveCommand));

            var move = sink.Commands.Should().ContainSingle(command => command is MakeMoveCommand).Subject as MakeMoveCommand?;
            move.Should().NotBeNull();
            move!.Value.CellId.Should().Be(new CellId(0, 1));
        }

        [Test]
        public async Task WhenBotKeepsTurn_ThenWaitsConfiguredDelayBetweenShots()
        {
            var snapshot = new FakeBattleshipSnapshotProvider
            {
                Phase = BattleshipPhase.Battle,
                ActivePlayerSlot = PlayerSlotMapping.SlotO,
                SlotXConfirmed = true,
                SlotOConfirmed = true,
                OpponentMarksForO = CreateMarksWithFiveUnknownCells(),
            };

            var battleshipEvents = new FakeBattleshipEventStream();
            var gameplayEvents = new FakeGameplayEventStream();
            var sink = new PassiveCapturingCommandSink();
            var sessionStore = new OnlineGameplaySessionContextStore();
            var autoPlacer = new BattleshipAutoPlacer(new BattleshipPlacementValidator());

            using var sut = new BattleshipBotDriver(
                snapshot,
                battleshipEvents,
                gameplayEvents,
                sink,
                autoPlacer,
                sessionStore,
                CreateSettings(botShotDelaySeconds: 0.25f));

            var config = new GameLaunchConfig(
                BattleshipStrategy.DefaultGameId,
                new BattleshipConfig(placementTimeLimitSeconds: 30),
                new BotOpponentConfig(BattleshipStrategy.DefaultBotDifficultyId),
                moveTimeLimitSeconds: 30);

            var result = await sut.StartAsync(config, PlayerSlotMapping.SlotO, CancellationToken.None);
            result.Status.Should().Be(BotStartStatus.Started);

            await UniTask.Delay(TimeSpan.FromMilliseconds(100));
            CountMoves(sink.Commands).Should().Be(1);

            await UniTask.Delay(TimeSpan.FromMilliseconds(220));
            CountMoves(sink.Commands).Should().BeGreaterThan(1);
        }

        [Test]
        public async Task WhenBotShootsMultipleTimes_ThenNoCellShotTwice()
        {
            var snapshot = new FakeBattleshipSnapshotProvider
            {
                Phase = BattleshipPhase.Battle,
                ActivePlayerSlot = PlayerSlotMapping.SlotO,
                SlotXConfirmed = true,
                SlotOConfirmed = true,
                OpponentMarksForO = CreateMarksWithFiveUnknownCells(),
            };

            var battleshipEvents = new FakeBattleshipEventStream();
            var gameplayEvents = new FakeGameplayEventStream();
            var sink = new CyclingCommandSink(snapshot);
            var sessionStore = new OnlineGameplaySessionContextStore();
            var autoPlacer = new BattleshipAutoPlacer(new BattleshipPlacementValidator());

            using var sut = new BattleshipBotDriver(
                snapshot,
                battleshipEvents,
                gameplayEvents,
                sink,
                autoPlacer,
                sessionStore,
                CreateSettings(botShotDelaySeconds: 0f));

            var config = new GameLaunchConfig(
                BattleshipStrategy.DefaultGameId,
                new BattleshipConfig(placementTimeLimitSeconds: 30),
                new BotOpponentConfig(BattleshipStrategy.DefaultBotDifficultyId),
                moveTimeLimitSeconds: 30);

            var result = await sut.StartAsync(config, PlayerSlotMapping.SlotO, CancellationToken.None);
            result.Status.Should().Be(BotStartStatus.Started);

            await WaitUntilAsync(() => CountMoves(sink.Commands) >= 5);

            var moves = sink.Commands.FindAll(command => command is MakeMoveCommand).ConvertAll(command => ((MakeMoveCommand)command).CellId);
            moves.Should().HaveCount(5);
            moves.Should().OnlyHaveUniqueItems();
            moves.Should().OnlyContain(cell => cell.Major == 0 && cell.Minor >= 0 && cell.Minor < 5);
        }

        [Test]
        public async Task WhenGameEndsInBattlePhase_ThenBotStopsSubmittingCommands()
        {
            var snapshot = new FakeBattleshipSnapshotProvider
            {
                Phase = BattleshipPhase.Battle,
                ActivePlayerSlot = PlayerSlotMapping.SlotO,
                SlotXConfirmed = true,
                SlotOConfirmed = true,
            };

            var battleshipEvents = new FakeBattleshipEventStream();
            var gameplayEvents = new FakeGameplayEventStream();
            var sink = new CyclingCommandSink(snapshot);
            var sessionStore = new OnlineGameplaySessionContextStore();
            var autoPlacer = new BattleshipAutoPlacer(new BattleshipPlacementValidator());

            using var sut = new BattleshipBotDriver(
                snapshot,
                battleshipEvents,
                gameplayEvents,
                sink,
                autoPlacer,
                sessionStore,
                CreateSettings(botShotDelaySeconds: 0f));

            var config = new GameLaunchConfig(
                BattleshipStrategy.DefaultGameId,
                new BattleshipConfig(placementTimeLimitSeconds: 30),
                new BotOpponentConfig(BattleshipStrategy.DefaultBotDifficultyId),
                moveTimeLimitSeconds: 30);

            var result = await sut.StartAsync(config, PlayerSlotMapping.SlotO, CancellationToken.None);
            result.Status.Should().Be(BotStartStatus.Started);

            await WaitUntilAsync(() => CountMoves(sink.Commands) >= 1);
            var moveCountBeforeFinish = CountMoves(sink.Commands);

            snapshot.Phase = BattleshipPhase.Finished;
            snapshot.CurrentStatus = GameStatus.Win;
            battleshipEvents.PublishPhase(BattleshipPhase.Finished);

            await UniTask.Yield();

            CountMoves(sink.Commands).Should().Be(moveCountBeforeFinish);
        }

        private static BattleshipCellMark[] CreateMarksWithSingleFinishCandidate()
        {
            var marks = new BattleshipCellMark[100];
            for (var i = 0; i < marks.Length; i++)
                marks[i] = BattleshipCellMark.Unknown;

            // Damaged ship at (0,0): only (0,1) remains a valid adjacent unknown target.
            marks[0] = BattleshipCellMark.Hit;
            marks[10] = BattleshipCellMark.Miss;

            return marks;
        }

        private static BattleshipCellMark[] CreateMarksWithFiveUnknownCells()
        {
            var marks = new BattleshipCellMark[100];
            for (var i = 0; i < marks.Length; i++)
                marks[i] = BattleshipCellMark.Miss;

            for (var i = 0; i < 5; i++)
                marks[i] = BattleshipCellMark.Unknown;

            return marks;
        }

        private static BattleshipGameplaySettings CreateSettings(float botShotDelaySeconds)
        {
            return BattleshipGameplaySettings.CreateRuntimeDefault(botShotDelaySeconds);
        }

        private static int CountMoves(IReadOnlyList<IGameplayCommand> commands)
        {
            var count = 0;
            for (var i = 0; i < commands.Count; i++)
            {
                if (commands[i] is MakeMoveCommand)
                    count++;
            }

            return count;
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
