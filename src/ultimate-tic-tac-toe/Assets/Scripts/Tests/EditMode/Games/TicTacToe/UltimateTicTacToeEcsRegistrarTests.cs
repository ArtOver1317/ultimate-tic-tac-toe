using FluentAssertions;
using NUnit.Framework;
using R3;
using Runtime.GameModes.Wizard;
using Runtime.Gameplay;
using Runtime.Gameplay.ECS;
using Runtime.Games.TicTacToe.ECS;
using Runtime.Games.TicTacToe.Ultimate;
using Runtime.Games.TicTacToe.Ultimate.Rules;
using Scellecs.Morpeh;
using CellId = Runtime.Games.TicTacToe.Moves.CellId;

namespace Tests.EditMode.Games.TicTacToe
{
    [TestFixture]
    [Category("Unit")]
    public class UltimateTicTacToeEcsRegistrarTests
    {
        private CommandQueue _commandQueue;
        private EventPublishSystem _eventSystem;
        private MatchEcsLifecycleService _lifecycle;
        private MatchStateProvider _stateProvider;

        [SetUp]
        public void SetUp()
        {
            _commandQueue = new CommandQueue();
            _eventSystem = new EventPublishSystem(new SynchronousEventScheduler());
            _lifecycle = new MatchEcsLifecycleService(
                new IEcsGameplayRegistrar[] { new UltimateTicTacToeEcsRegistrar(new UltimateRulesEngine()) },
                _commandQueue,
                _eventSystem);
            _stateProvider = new MatchStateProvider(_commandQueue, _lifecycle, _eventSystem);
        }

        [TearDown]
        public void TearDown()
        {
            _stateProvider?.Dispose();
            _lifecycle?.Dispose();
        }

        [Test]
        public void WhenUltimateMatchStarted_ThenBoardContains81Cells()
        {
            _lifecycle.StartMatch(new GameLaunchConfig(
                UltimateTicTacToeStrategy.DefaultGameId,
                UltimateTicTacToeConfig.Instance,
                new LocalHumanConfig()));

            var allCells = _stateProvider.GetAllCells();

            allCells.Should().HaveCount(81);
            allCells.Should().OnlyContain(c => c.Slot == -1);
        }

        [Test]
        public void WhenTwoMovesSubmitted_ThenAppliesXThenOAndSwitchesActivePlayer()
        {
            _lifecycle.StartMatch(new GameLaunchConfig(
                UltimateTicTacToeStrategy.DefaultGameId,
                UltimateTicTacToeConfig.Instance,
                new LocalHumanConfig()));

            _stateProvider.ActivePlayerSlot.Should().Be(PlayerSlotMapping.SlotX);

            _stateProvider.SubmitCommand(new MakeMoveCommand(new CellId(0, 0)));
            _stateProvider.GetCellSlot(new CellId(0, 0)).Should().Be(PlayerSlotMapping.SlotX);
            _stateProvider.ActivePlayerSlot.Should().Be(PlayerSlotMapping.SlotO);

            _stateProvider.SubmitCommand(new MakeMoveCommand(new CellId(0, 1)));
            _stateProvider.GetCellSlot(new CellId(0, 1)).Should().Be(PlayerSlotMapping.SlotO);
            _stateProvider.ActivePlayerSlot.Should().Be(PlayerSlotMapping.SlotX);
        }

        [Test]
        public void WhenUltimateMatchStarted_ThenInitializesUltimateStateComponents()
        {
            _lifecycle.StartMatch(new GameLaunchConfig(
                UltimateTicTacToeStrategy.DefaultGameId,
                UltimateTicTacToeConfig.Instance,
                new LocalHumanConfig()));

            var world = _lifecycle.World;
            var entity = _lifecycle.MatchEntity;

            var allowedStash = world.GetStash<UltimateAllowedMajorsComponent>();
            var miniBoardsStash = world.GetStash<UltimateMiniBoardsComponent>();
            var winLineStash = world.GetStash<UltimateBigBoardWinLineComponent>();
            var epochStash = world.GetStash<UltimateEpochComponent>();

            allowedStash.Has(entity).Should().BeTrue();
            miniBoardsStash.Has(entity).Should().BeTrue();
            winLineStash.Has(entity).Should().BeTrue();
            epochStash.Has(entity).Should().BeTrue();

            allowedStash.Get(entity).Value.Should().Be(AllowedMajors.All);
            epochStash.Get(entity).Value.Should().Be(0UL);
            winLineStash.Get(entity).HasValue.Should().BeFalse();
            miniBoardsStash.Get(entity).Statuses.Should().HaveCount(9).And.OnlyContain(s => s == MiniBoardStatus.InProgress);
        }

        [Test]
        public void WhenMoveTargetsNotAllowedMajor_ThenRejectsWithForbiddenMove()
        {
            _lifecycle.StartMatch(new GameLaunchConfig(
                UltimateTicTacToeStrategy.DefaultGameId,
                UltimateTicTacToeConfig.Instance,
                new LocalHumanConfig()));

            var world = _lifecycle.World;
            var entity = _lifecycle.MatchEntity;
            var allowedStash = world.GetStash<UltimateAllowedMajorsComponent>();
            allowedStash.Get(entity).Value = new AllowedMajors((ushort)(1 << 1));

            using var rejections = new CompositeDisposable();
            CommandRejectedEvent? rejected = null;
            _stateProvider.CommandRejected.Subscribe(evt => rejected = evt).AddTo(rejections);

            _stateProvider.SubmitCommand(new MakeMoveCommand(new CellId(2, 0)));

            rejected.Should().NotBeNull();
            rejected!.Value.Rejection.Reason.Should().Be(GameplayRejectionReason.ForbiddenMove);
            _stateProvider.GetCellSlot(new CellId(2, 0)).Should().Be(-1);
            _stateProvider.ActivePlayerSlot.Should().Be(PlayerSlotMapping.SlotX);
        }

        [Test]
        public void WhenMoveTargetsClosedMiniBoard_ThenRejectsWithForbiddenMove()
        {
            _lifecycle.StartMatch(new GameLaunchConfig(
                UltimateTicTacToeStrategy.DefaultGameId,
                UltimateTicTacToeConfig.Instance,
                new LocalHumanConfig()));

            var world = _lifecycle.World;
            var entity = _lifecycle.MatchEntity;

            var allowedStash = world.GetStash<UltimateAllowedMajorsComponent>();
            var miniBoardsStash = world.GetStash<UltimateMiniBoardsComponent>();

            allowedStash.Get(entity).Value = new AllowedMajors((ushort)(1 << 4));
            miniBoardsStash.Get(entity).Statuses[4] = MiniBoardStatus.WonByX;

            using var rejections = new CompositeDisposable();
            CommandRejectedEvent? rejected = null;
            _stateProvider.CommandRejected.Subscribe(evt => rejected = evt).AddTo(rejections);

            _stateProvider.SubmitCommand(new MakeMoveCommand(new CellId(4, 0)));

            rejected.Should().NotBeNull();
            rejected!.Value.Rejection.Reason.Should().Be(GameplayRejectionReason.ForbiddenMove);
            _stateProvider.GetCellSlot(new CellId(4, 0)).Should().Be(-1);
        }

        [Test]
        public void WhenMoveClosesMiniBoard_ThenUpdatesMiniStatusAndAllowedMajors()
        {
            _lifecycle.StartMatch(new GameLaunchConfig(
                UltimateTicTacToeStrategy.DefaultGameId,
                UltimateTicTacToeConfig.Instance,
                new LocalHumanConfig()));

            var world = _lifecycle.World;
            var entity = _lifecycle.MatchEntity;

            var boardStash = world.GetStash<BoardStateComponent>();
            var miniBoardsStash = world.GetStash<UltimateMiniBoardsComponent>();
            var allowedStash = world.GetStash<UltimateAllowedMajorsComponent>();

            ref var board = ref boardStash.Get(entity);
            board.Cells[7 * board.MinorCount + 0] = Runtime.Games.TicTacToe.Moves.PlayerMark.X;
            board.Cells[7 * board.MinorCount + 1] = Runtime.Games.TicTacToe.Moves.PlayerMark.X;

            _stateProvider.SubmitCommand(new MakeMoveCommand(new CellId(7, 2)));

            miniBoardsStash.Get(entity).Statuses[7].Should().Be(MiniBoardStatus.WonByX);
            allowedStash.Get(entity).Value.Mask.Should().Be((ushort)(1 << 2));
        }

        [Test]
        public void WhenMoveCompletesBigBoardWin_ThenPublishesRoundFinishedAndStoresBigBoardLine()
        {
            _lifecycle.StartMatch(new GameLaunchConfig(
                UltimateTicTacToeStrategy.DefaultGameId,
                UltimateTicTacToeConfig.Instance,
                new LocalHumanConfig()));

            var world = _lifecycle.World;
            var entity = _lifecycle.MatchEntity;

            var boardStash = world.GetStash<BoardStateComponent>();
            var miniBoardsStash = world.GetStash<UltimateMiniBoardsComponent>();
            var statusStash = world.GetStash<MatchStatusComponent>();
            var bigLineStash = world.GetStash<UltimateBigBoardWinLineComponent>();

            miniBoardsStash.Get(entity).Statuses[0] = MiniBoardStatus.WonByX;
            miniBoardsStash.Get(entity).Statuses[1] = MiniBoardStatus.WonByX;

            ref var board = ref boardStash.Get(entity);
            board.Cells[2 * board.MinorCount + 0] = Runtime.Games.TicTacToe.Moves.PlayerMark.X;
            board.Cells[2 * board.MinorCount + 1] = Runtime.Games.TicTacToe.Moves.PlayerMark.X;

            RoundFinishedEvent? finished = null;
            using var sub = _stateProvider.RoundFinished.Subscribe(evt => finished = evt);

            _stateProvider.SubmitCommand(new MakeMoveCommand(new CellId(2, 2)));

            finished.Should().NotBeNull();
            finished!.Value.Status.Should().Be(GameStatus.Win);
            finished!.Value.WinnerSlot.Should().Be(PlayerSlotMapping.SlotX);

            statusStash.Get(entity).Status.Should().Be(GameStatus.Win);
            statusStash.Get(entity).WinnerSlot.Should().Be(PlayerSlotMapping.SlotX);
            bigLineStash.Get(entity).HasValue.Should().BeTrue();
            bigLineStash.Get(entity).Value.Should().Be(new UltimateBigBoardWinLine(0, 1, 2));
        }

        [Test]
        public void WhenRestartRound_ThenResetsUltimateStateAndIncrementsEpoch()
        {
            _lifecycle.StartMatch(new GameLaunchConfig(
                UltimateTicTacToeStrategy.DefaultGameId,
                UltimateTicTacToeConfig.Instance,
                new LocalHumanConfig()));

            var world = _lifecycle.World;
            var entity = _lifecycle.MatchEntity;

            var allowedStash = world.GetStash<UltimateAllowedMajorsComponent>();
            var miniBoardsStash = world.GetStash<UltimateMiniBoardsComponent>();
            var bigLineStash = world.GetStash<UltimateBigBoardWinLineComponent>();
            var epochStash = world.GetStash<UltimateEpochComponent>();

            allowedStash.Get(entity).Value = AllowedMajors.None;
            miniBoardsStash.Get(entity).Statuses[3] = MiniBoardStatus.WonByO;
            bigLineStash.Get(entity).HasValue = true;
            bigLineStash.Get(entity).Value = new UltimateBigBoardWinLine(0, 4, 8);

            _stateProvider.SubmitCommand(new RestartRoundCommand(PlayerSlotMapping.SlotO));

            epochStash.Get(entity).Value.Should().Be(1UL);
            allowedStash.Get(entity).Value.Should().Be(AllowedMajors.All);
            miniBoardsStash.Get(entity).Statuses.Should().OnlyContain(s => s == MiniBoardStatus.InProgress);
            bigLineStash.Get(entity).HasValue.Should().BeFalse();
            _stateProvider.ActivePlayerSlot.Should().Be(PlayerSlotMapping.SlotO);
        }

        [Test]
        public void WhenMoveClosesMiniBoard_ThenPublishesUltimateEventsWithCurrentEpoch()
        {
            _lifecycle.StartMatch(new GameLaunchConfig(
                UltimateTicTacToeStrategy.DefaultGameId,
                UltimateTicTacToeConfig.Instance,
                new LocalHumanConfig()));

            var world = _lifecycle.World;
            var entity = _lifecycle.MatchEntity;
            var boardStash = world.GetStash<BoardStateComponent>();

            ref var board = ref boardStash.Get(entity);
            board.Cells[7 * board.MinorCount + 0] = Runtime.Games.TicTacToe.Moves.PlayerMark.X;
            board.Cells[7 * board.MinorCount + 1] = Runtime.Games.TicTacToe.Moves.PlayerMark.X;

            var ultimateStream = (IUltimateGameplayEventStream)_stateProvider;

            AllowedMajorsChangedEvent? allowedEvt = null;
            MiniBoardStatusChangedEvent? miniEvt = null;
            using var d = new CompositeDisposable();
            ultimateStream.AllowedMajorsChanged.Subscribe(evt => allowedEvt = evt).AddTo(d);
            ultimateStream.MiniBoardStatusChanged.Subscribe(evt => miniEvt = evt).AddTo(d);

            _stateProvider.SubmitCommand(new MakeMoveCommand(new CellId(7, 2)));

            allowedEvt.Should().NotBeNull();
            allowedEvt!.Value.Epoch.Should().Be(0UL);
            allowedEvt!.Value.AllowedMajors.Should().Be(new AllowedMajors((ushort)(1 << 2)));

            miniEvt.Should().NotBeNull();
            miniEvt!.Value.Epoch.Should().Be(0UL);
            miniEvt!.Value.Major.Should().Be(7);
            miniEvt!.Value.NewStatus.Should().Be(MiniBoardStatus.WonByX);
        }

        [Test]
        public void WhenRestartRound_ThenUltimateSnapshotProviderReturnsResetState()
        {
            _lifecycle.StartMatch(new GameLaunchConfig(
                UltimateTicTacToeStrategy.DefaultGameId,
                UltimateTicTacToeConfig.Instance,
                new LocalHumanConfig()));

            var snapshot = (IUltimateGameplaySnapshotProvider)_stateProvider;
            var miniBoards = new MiniBoardStatus[9];

            _stateProvider.SubmitCommand(new RestartRoundCommand(PlayerSlotMapping.SlotO));

            snapshot.Epoch.Should().Be(1UL);
            snapshot.CurrentAllowedMajors.Should().Be(AllowedMajors.All);
            snapshot.CopyMiniBoardsTo(miniBoards);
            miniBoards.Should().OnlyContain(s => s == MiniBoardStatus.InProgress);
        }
    }
}
