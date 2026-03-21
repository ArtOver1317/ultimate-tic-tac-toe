#nullable enable

using System;
using FluentAssertions;
using NUnit.Framework;
using Runtime.GameModes.Wizard.Online;

namespace Tests.EditMode.GameModes.Wizard.Online.Gameplay
{
    [TestFixture]
    [Category("Unit")]
    public class HostAuthoritativeMoveProcessorTests
    {
        [Test]
        public void WhenValidMoveFromActivePlayer_ThenAcceptsAndSwitchesTurn()
        {
            // Arrange
            var state = new AuthoritativeMatchState(9, "host");
            var sut = new HostAuthoritativeMoveProcessor();
            var cmd = new MoveCommand(Guid.NewGuid(), "host", 0, 1);

            // Act
            var result = sut.Process(cmd, state, "guest");

            // Assert
            result.Status.Should().Be(MoveProcessStatus.Accepted);
            state.IsCellOccupied(0).Should().BeTrue();
            state.ActivePlayerUserId.Should().Be("guest");
        }

        [Test]
        public void WhenMoveOutOfTurn_ThenRejectedWithNotPlayerTurn()
        {
            // Arrange
            var state = new AuthoritativeMatchState(9, "host");
            var sut = new HostAuthoritativeMoveProcessor();
            var cmd = new MoveCommand(Guid.NewGuid(), "guest", 0, 1);

            // Act
            var result = sut.Process(cmd, state, "host");

            // Assert
            result.Status.Should().Be(MoveProcessStatus.Rejected);
            result.RejectReason.Should().Be(MoveRejectReason.NotPlayerTurn);
        }

        [Test]
        public void WhenMoveTargetsOccupiedCell_ThenRejectedWithCellAlreadyOccupied()
        {
            // Arrange
            var state = new AuthoritativeMatchState(9, "host");
            state.MarkCellOccupied(4);
            var sut = new HostAuthoritativeMoveProcessor();
            var cmd = new MoveCommand(Guid.NewGuid(), "host", 4, 1);

            // Act
            var result = sut.Process(cmd, state, "guest");

            // Assert
            result.Status.Should().Be(MoveProcessStatus.Rejected);
            result.RejectReason.Should().Be(MoveRejectReason.CellAlreadyOccupied);
        }

        [Test]
        public void WhenDuplicateCommand_ThenIgnoredIdempotently()
        {
            // Arrange
            var state = new AuthoritativeMatchState(9, "host");
            var sut = new HostAuthoritativeMoveProcessor();
            var commandId = Guid.NewGuid();
            var first = new MoveCommand(commandId, "host", 2, 1);

            // Act
            var firstResult = sut.Process(first, state, "guest");
            state.SetActivePlayer("host");
            var duplicate = new MoveCommand(commandId, "host", 3, 2);
            var duplicateResult = sut.Process(duplicate, state, "guest");

            // Assert
            firstResult.Status.Should().Be(MoveProcessStatus.Accepted);
            duplicateResult.Status.Should().Be(MoveProcessStatus.DuplicateIgnored);
            state.IsCellOccupied(3).Should().BeFalse();
        }

        [Test]
        public void WhenMoveSubmittedAfterMatchCompleted_ThenRejectedWithMatchAlreadyFinished()
        {
            // Arrange
            var state = new AuthoritativeMatchState(9, "host");
            state.Complete();
            var sut = new HostAuthoritativeMoveProcessor();
            var command = new MoveCommand(Guid.NewGuid(), "host", 1, 7);

            // Act
            var result = sut.Process(command, state, "guest");

            // Assert
            result.Status.Should().Be(MoveProcessStatus.Rejected);
            result.RejectReason.Should().Be(MoveRejectReason.MatchAlreadyFinished);
        }

        [Test]
        public void WhenDedupWindowExceedsCapacity_ThenOldestCommandIdIsEvictedAndAcceptedAgain()
        {
            // Arrange
            var state = new AuthoritativeMatchState(9, "host");
            var sut = new HostAuthoritativeMoveProcessor(dedupWindowSize: 3);

            var firstId = Guid.NewGuid();
            var secondId = Guid.NewGuid();
            var thirdId = Guid.NewGuid();
            var fourthId = Guid.NewGuid();

            // Act
            var first = sut.Process(new MoveCommand(firstId, "host", 0, 1), state, "guest");
            state.SetActivePlayer("host");
            var second = sut.Process(new MoveCommand(secondId, "host", 1, 2), state, "guest");
            state.SetActivePlayer("host");
            var third = sut.Process(new MoveCommand(thirdId, "host", 2, 3), state, "guest");
            state.SetActivePlayer("host");
            var fourth = sut.Process(new MoveCommand(fourthId, "host", 3, 4), state, "guest");
            state.SetActivePlayer("host");

            var reusedFirst = sut.Process(new MoveCommand(firstId, "host", 4, 5), state, "guest");

            // Assert
            first.Status.Should().Be(MoveProcessStatus.Accepted);
            second.Status.Should().Be(MoveProcessStatus.Accepted);
            third.Status.Should().Be(MoveProcessStatus.Accepted);
            fourth.Status.Should().Be(MoveProcessStatus.Accepted);
            reusedFirst.Status.Should().Be(MoveProcessStatus.Accepted);
            state.IsCellOccupied(4).Should().BeTrue();
        }
    }
}