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
        public void WhenValidMove_ThenCellChangedWithCorrectSlot()
        {
            StartMatch();

            PlayMove(0, 0);

            var cell = EventsOf<CellChangedEvent>().Should().ContainSingle().Which;
            cell.CellId.Should().Be(new CellId(0, 0));
            cell.NewSlot.Should().Be(TicTacToeEcsRegistrar.SlotX);
        }

        [Test]
        public void WhenValidMove_ThenEventsInDeterministicOrder()
        {
            StartMatch();

            PlayMove(0, 0);

            _events.Should().HaveCount(3);
            _events[0].Should().BeOfType<CellChangedEvent>();
            _events[1].Should().BeOfType<LastMoveChangedEvent>();
            _events[2].Should().BeOfType<CurrentPlayerChangedEvent>();
        }

        [Test]
        public void WhenValidMove_ThenLastMoveUpdated()
        {
            StartMatch();

            PlayMove(1, 2);

            var lm = EventsOf<LastMoveChangedEvent>().Should().ContainSingle().Which;
            lm.CellId.Should().Be(new CellId(1, 2));
        }

        [Test]
        public void WhenValidMove_ThenCurrentPlayerSwitched()
        {
            StartMatch();

            PlayMove(0, 0);

            var cp = EventsOf<CurrentPlayerChangedEvent>().Should().ContainSingle().Which;
            cp.ActivePlayerSlot.Should().Be(TicTacToeEcsRegistrar.SlotO);
        }

        [Test]
        public void WhenTwoMoves_ThenPlayersAlternate()
        {
            StartMatch();
            PlayMove(0, 0);
            ClearEvents();

            PlayMove(1, 0);

            var cell = EventsOf<CellChangedEvent>().Should().ContainSingle().Which;
            cell.NewSlot.Should().Be(TicTacToeEcsRegistrar.SlotO);
            var cp = EventsOf<CurrentPlayerChangedEvent>().Should().ContainSingle().Which;
            cp.ActivePlayerSlot.Should().Be(TicTacToeEcsRegistrar.SlotX);
        }
    }
}