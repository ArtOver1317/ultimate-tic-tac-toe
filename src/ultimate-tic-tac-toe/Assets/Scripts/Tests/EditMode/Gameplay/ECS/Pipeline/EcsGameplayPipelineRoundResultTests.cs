#nullable enable

using FluentAssertions;
using NUnit.Framework;
using Runtime.Gameplay.Shared;
using Runtime.Games.TicTacToe.ECS;
using CellId = Runtime.Gameplay.CellId;
using EcsGameStatus = Runtime.Gameplay.Shared.EcsGameStatus;

namespace Tests.EditMode.Gameplay.ECS.Pipeline
{
    public partial class EcsGameplayPipelineTests
    {
        [Test]
        public void WhenTimeoutCommandSubmitted_ThenRoundFinishedWithTimeoutAndFirstNonLoserWinner()
        {
            StartMatch();

            _stateProvider.SubmitCommand(new TimeoutCommand(TicTacToeEcsRegistrar.SlotX));

            var rf = EventsOf<RoundFinishedEvent>().Should().ContainSingle().Which;
            rf.Status.Should().Be(EcsGameStatus.Timeout);
            rf.WinnerSlot.Should().Be(TicTacToeEcsRegistrar.SlotO);
            rf.WinLine.Should().BeNull();
        }

        [Test]
        public void WhenTimeoutCommandSubmittedAfterWin_ThenTimeoutIgnored()
        {
            StartMatch();
            PlayMove(0, 0);
            PlayMove(1, 0);
            PlayMove(0, 1);
            PlayMove(1, 1);
            PlayMove(0, 2);
            ClearEvents();

            _stateProvider.SubmitCommand(new TimeoutCommand(TicTacToeEcsRegistrar.SlotO));

            EventsOf<RoundFinishedEvent>().Should().BeEmpty();
        }

        [Test]
        public void WhenXCompletesTopRow_ThenRoundFinishedWithWin()
        {
            StartMatch();
            PlayMove(0, 0);
            PlayMove(1, 0);
            PlayMove(0, 1);
            PlayMove(1, 1);
            ClearEvents();

            PlayMove(0, 2);

            _events.Should().HaveCount(4);
            _events[3].Should().BeOfType<RoundFinishedEvent>();

            var rf = (RoundFinishedEvent)_events[3];
            rf.Status.Should().Be(EcsGameStatus.Win);
            rf.WinnerSlot.Should().Be(TicTacToeEcsRegistrar.SlotX);
        }

        [Test]
        public void WhenXWins_ThenWinLineReported()
        {
            StartMatch();
            PlayMove(0, 0);
            PlayMove(1, 0);
            PlayMove(0, 1);
            PlayMove(1, 1);
            PlayMove(0, 2);

            var rf = EventsOf<RoundFinishedEvent>().Should().ContainSingle().Which;
            rf.WinLine.Should().NotBeNull();
            rf.WinLine!.Value.Start.Should().Be(new CellId(0, 0));
            rf.WinLine!.Value.End.Should().Be(new CellId(0, 2));
        }

        [Test]
        public void WhenBoardFullWithoutWinner_ThenRoundFinishedWithDraw()
        {
            StartMatch();
            PlayMove(0, 0);
            PlayMove(0, 1);
            PlayMove(1, 1);
            PlayMove(2, 2);
            PlayMove(0, 2);
            PlayMove(2, 0);
            PlayMove(1, 0);
            PlayMove(1, 2);
            ClearEvents();

            PlayMove(2, 1);

            var rf = EventsOf<RoundFinishedEvent>().Should().ContainSingle().Which;
            rf.Status.Should().Be(EcsGameStatus.Draw);
            rf.WinnerSlot.Should().BeNull();
            rf.WinLine.Should().BeNull();
        }

        [Test]
        public void WhenWinMove_ThenEventsInDeterministicOrderIncludingRoundFinished()
        {
            StartMatch();
            PlayMove(0, 0);
            PlayMove(1, 0);
            PlayMove(0, 1);
            PlayMove(1, 1);
            ClearEvents();

            PlayMove(0, 2);

            _events.Should().HaveCount(4);
            _events[0].Should().BeOfType<CellChangedEvent>();
            _events[1].Should().BeOfType<LastMoveChangedEvent>();
            _events[2].Should().BeOfType<CurrentPlayerChangedEvent>();
            _events[3].Should().BeOfType<RoundFinishedEvent>();
        }
    }
}