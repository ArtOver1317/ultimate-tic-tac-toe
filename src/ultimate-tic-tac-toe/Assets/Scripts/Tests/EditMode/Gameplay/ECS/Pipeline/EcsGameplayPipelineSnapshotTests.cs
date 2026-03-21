#nullable enable

using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Runtime.Games.TicTacToe.ECS;
using CellId = Runtime.Gameplay.CellId;

namespace Tests.EditMode.Gameplay.ECS.Pipeline
{
    public partial class EcsGameplayPipelineTests
    {
        [Test]
        public void WhenMoveApplied_ThenGetCellSlotReturnsCorrectSlot()
        {
            StartMatch();

            PlayMove(0, 0);

            _stateProvider.GetCellSlot(new CellId(0, 0)).Should().Be(TicTacToeEcsRegistrar.SlotX);
            _stateProvider.GetCellSlot(new CellId(0, 1)).Should().Be(-1, "empty cell");
        }

        [Test]
        public void WhenMoveApplied_ThenGetAllCellsReflectsBoard()
        {
            StartMatch();
            PlayMove(0, 0);
            PlayMove(1, 1);

            var cells = _stateProvider.GetAllCells();
            cells.Should().HaveCount(9);
            cells.First(c => c.CellId == new CellId(0, 0)).Slot.Should().Be(TicTacToeEcsRegistrar.SlotX);
            cells.First(c => c.CellId == new CellId(1, 1)).Slot.Should().Be(TicTacToeEcsRegistrar.SlotO);
           
            cells.Where(c => c.CellId != new CellId(0, 0) && c.CellId != new CellId(1, 1))
                .Should().OnlyContain(c => c.Slot == -1);
        }

        [Test]
        public void WhenMoveApplied_ThenCommandSequenceIncrements()
        {
            StartMatch();
            _stateProvider.CommandSequence.Should().Be(0);

            PlayMove(0, 0);
            _stateProvider.CommandSequence.Should().Be(1);

            PlayMove(1, 0);
            _stateProvider.CommandSequence.Should().Be(2);
        }

        [Test]
        public void WhenMatchNotActive_ThenSnapshotsReturnDefaults()
        {
            _stateProvider.GetCellSlot(new CellId(0, 0)).Should().Be(-1);
            _stateProvider.GetAllCells().Should().BeEmpty();
            _stateProvider.CommandSequence.Should().Be(-1);
        }
    }
}