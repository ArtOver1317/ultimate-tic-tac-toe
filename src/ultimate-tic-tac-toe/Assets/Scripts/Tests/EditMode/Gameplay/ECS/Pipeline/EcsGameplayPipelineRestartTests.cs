#nullable enable

using FluentAssertions;
using NUnit.Framework;
using Runtime.Gameplay.Shared;
using Runtime.Games.TicTacToe.ECS;
using CellId = Runtime.Gameplay.CellId;

namespace Tests.EditMode.Gameplay.ECS.Pipeline
{
    public partial class EcsGameplayPipelineTests
    {
        [Test]
        public void WhenRestartRound_ThenBoardCleared()
        {
            StartMatch();
            PlayMove(0, 0);
            PlayMove(1, 0);

            _stateProvider.SubmitCommand(new RestartRoundCommand(TicTacToeEcsRegistrar.SlotX));

            var cells = _stateProvider.GetAllCells();
            cells.Should().OnlyContain(c => c.Slot == -1, "all cells should be empty after restart");
        }

        [Test]
        public void WhenRestartRound_ThenLastMoveReset()
        {
            StartMatch();
            PlayMove(0, 0);
            ClearEvents();

            _stateProvider.SubmitCommand(new RestartRoundCommand(TicTacToeEcsRegistrar.SlotO));

            ClearEvents();
            PlayMove(1, 1);

            var lm = EventsOf<LastMoveChangedEvent>().Should().ContainSingle().Which;
            lm.CellId.Should().Be(new CellId(1, 1));
        }

        [Test]
        public void WhenRestartRoundWithSlotO_ThenOMovesFirst()
        {
            StartMatch();
            PlayMove(0, 0);

            _stateProvider.SubmitCommand(new RestartRoundCommand(TicTacToeEcsRegistrar.SlotO));
            ClearEvents();

            PlayMove(1, 1);

            var cell = EventsOf<CellChangedEvent>().Should().ContainSingle().Which;
            cell.NewSlot.Should().Be(TicTacToeEcsRegistrar.SlotO);
        }

        [Test]
        public void WhenRestartAfterWin_ThenNewMovesAllowed()
        {
            StartMatch();
            PlayMove(0, 0);
            PlayMove(1, 0);
            PlayMove(0, 1);
            PlayMove(1, 1);
            PlayMove(0, 2);

            _stateProvider.SubmitCommand(new RestartRoundCommand(TicTacToeEcsRegistrar.SlotO));
            ClearEvents();

            PlayMove(2, 2);

            EventsOf<CommandRejectedEvent>().Should().BeEmpty("moves should be allowed after restart");
            EventsOf<CellChangedEvent>().Should().ContainSingle();
        }
    }
}