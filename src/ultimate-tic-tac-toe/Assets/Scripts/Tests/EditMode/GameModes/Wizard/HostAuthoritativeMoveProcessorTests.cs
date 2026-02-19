#nullable enable

using System;
using FluentAssertions;
using NUnit.Framework;
using Runtime.GameModes.Wizard;

namespace Tests.EditMode.GameModes.Wizard
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
    }
}

#nullable restore