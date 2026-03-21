#nullable enable

using FluentAssertions;
using NUnit.Framework;
using Runtime.Gameplay.Shared;
using UnityEngine.TestTools;
using CellId = Runtime.Gameplay.CellId;

namespace Tests.EditMode.Gameplay.ECS.Pipeline
{
    public partial class EcsGameplayPipelineTests
    {
        [Test]
        public void WhenMatchNotActive_ThenCommandRejectedWithMatchNotActive()
        {
            _stateProvider.SubmitCommand(new MakeMoveCommand(new CellId(0, 0)));

            var rej = EventsOf<CommandRejectedEvent>().Should().ContainSingle().Which;
            rej.Rejection.Reason.Should().Be(GameplayRejectionReason.MatchNotActive);
        }

        [Test]
        public void WhenCellOccupied_ThenCommandRejectedWithCellOccupied()
        {
            StartMatch();
            PlayMove(0, 0);
            ClearEvents();

            PlayMove(0, 0);

            var rej = EventsOf<CommandRejectedEvent>().Should().ContainSingle().Which;
            rej.Rejection.Reason.Should().Be(GameplayRejectionReason.CellOccupied);
        }

        [Test]
        public void WhenInvalidCell_ThenCommandRejectedWithInvalidCell()
        {
            StartMatch();

            PlayMove(5, 5);

            var rej = EventsOf<CommandRejectedEvent>().Should().ContainSingle().Which;
            rej.Rejection.Reason.Should().Be(GameplayRejectionReason.InvalidCell);
        }

        [Test]
        public void WhenNegativeCell_ThenCommandRejectedWithInvalidCell()
        {
            StartMatch();

            PlayMove(-1, 0);

            var rej = EventsOf<CommandRejectedEvent>().Should().ContainSingle().Which;
            rej.Rejection.Reason.Should().Be(GameplayRejectionReason.InvalidCell);
        }

        [Test]
        public void WhenRoundAlreadyEnded_ThenCommandRejectedWithRoundAlreadyEnded()
        {
            StartMatch();
            PlayMove(0, 0);
            PlayMove(1, 0);
            PlayMove(0, 1);
            PlayMove(1, 1);
            PlayMove(0, 2);
            ClearEvents();

            PlayMove(2, 2);

            var rej = EventsOf<CommandRejectedEvent>().Should().ContainSingle().Which;
            rej.Rejection.Reason.Should().Be(GameplayRejectionReason.RoundAlreadyEnded);
        }

        [Test]
        public void WhenTimeoutCommandHasInvalidLoserSlot_ThenTimeoutIgnored()
        {
            StartMatch();

            LogAssert.Expect(UnityEngine.LogType.Error,
                "[Infrastructure] [TimeoutTerminalSystem] Invalid LoserSlot=999. Timeout ignored.");

            _stateProvider.SubmitCommand(new TimeoutCommand(999));

            EventsOf<RoundFinishedEvent>().Should().BeEmpty();
        }
    }
}