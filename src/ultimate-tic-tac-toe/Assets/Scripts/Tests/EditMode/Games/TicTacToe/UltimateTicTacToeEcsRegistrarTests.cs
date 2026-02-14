using FluentAssertions;
using NUnit.Framework;
using Runtime.GameModes.Wizard;
using Runtime.Gameplay;
using Runtime.Gameplay.ECS;
using Runtime.Games.TicTacToe.ECS;
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
                new IEcsGameplayRegistrar[] { new UltimateTicTacToeEcsRegistrar() },
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
    }
}
