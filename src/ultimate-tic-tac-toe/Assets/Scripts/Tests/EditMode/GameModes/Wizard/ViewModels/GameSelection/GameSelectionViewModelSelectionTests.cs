using System;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using R3;
using Runtime.GameModes.Wizard.Coordinator;
using Runtime.GameModes.Wizard.Session;

namespace Tests.EditMode.GameModes.Wizard.ViewModels.GameSelection
{
    public partial class GameSelectionViewModelTests
    {
        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void WhenSelectModeCalledWithNullOrWhitespace_ThenSetsSelectedModeIdToNullAndCanContinueFalse(string gameId)
        {
            // Arrange
            var modes = CreateModes("classic");
            _catalog.Metadata.Returns(modes);

            using var sut = CreateSut();
            sut.Initialize();

            // Act
            sut.SelectMode(gameId);

            // Assert
            sut.SelectedGameId.Value.Should().BeNull();
            sut.CanContinue.CurrentValue.Should().BeFalse();
        }

        [Test]
        public void WhenSelectModeCalledWithValidModeId_ThenSetsSelectedModeIdAndCanContinueTrue()
        {
            // Arrange
            var modes = CreateModes("classic");
            _catalog.Metadata.Returns(modes);

            using var sut = CreateSut();
            sut.Initialize();

            // Act
            sut.SelectMode("classic");

            // Assert
            sut.SelectedGameId.Value.Should().Be("classic");
            sut.CanContinue.CurrentValue.Should().BeTrue();
        }

        [Test]
        public void WhenSelectModeCalledMultipleTimes_ThenUpdatesSelectionAndCanContinueReflectsLastState()
        {
            // Arrange
            var modes = CreateModes("classic", "ultimate");
            _catalog.Metadata.Returns(modes);

            using var sut = CreateSut();
            sut.Initialize();

            // Act
            sut.SelectMode("classic");
            sut.SelectedGameId.Value.Should().Be("classic");

            sut.SelectMode("ultimate");
            sut.SelectedGameId.Value.Should().Be("ultimate");

            sut.SelectMode(null);

            // Assert
            sut.SelectedGameId.Value.Should().BeNull();
            sut.CanContinue.CurrentValue.Should().BeFalse();
        }

        [Test]
        public void WhenSelectModeCalledWithSameValueTwice_ThenDoesNotEmitDuplicateUpdates()
        {
            // Arrange
            var modes = CreateModes("classic");
            _catalog.Metadata.Returns(modes);

            using var sut = CreateSut();
            sut.Initialize();

            var emitCount = 0;
            using var sub = sut.SelectedGameId.Subscribe(_ => emitCount++);
            emitCount = 0;

            // Act
            sut.SelectMode("classic");
            sut.SelectMode("classic");

            // Assert
            emitCount.Should().Be(1);
        }

        [Test]
        public void WhenRequestContinueCalledWithNoSelection_ThenDoesNotPublishIntent()
        {
            // Arrange
            var modes = CreateModes("classic");
            _catalog.Metadata.Returns(modes);

            using var sut = CreateSut();
            sut.Initialize();

            // Act
            sut.RequestContinue();

            // Assert
            _coordinator.DidNotReceive().TryPublishIntent(WizardIntent.Continue);
        }

        [Test]
        public void WhenRequestContinueCalledWithValidSelection_ThenPublishesContinueIntent()
        {
            // Arrange
            var modes = CreateModes("classic");
            _catalog.Metadata.Returns(modes);
            _coordinator.TryPublishIntent(WizardIntent.Continue).Returns(true);

            using var sut = CreateSut();
            sut.Initialize();
            sut.SelectMode("classic");

            // Act
            sut.RequestContinue();

            // Assert
            _coordinator.Received(1).TryPublishIntent(WizardIntent.Continue);
        }

        [Test]
        public void WhenRequestContinueCalledAndCoordinatorRejectsIntent_ThenDoesNotThrow()
        {
            // Arrange
            var modes = CreateModes("classic");
            _catalog.Metadata.Returns(modes);
            _coordinator.TryPublishIntent(WizardIntent.Continue).Returns(false);

            using var sut = CreateSut();
            sut.Initialize();
            sut.SelectMode("classic");

            // Act
            Action act = sut.RequestContinue;

            // Assert
            act.Should().NotThrow();
            _coordinator.Received(1).TryPublishIntent(WizardIntent.Continue);
        }

        [Test]
        public void WhenRequestCancelCalled_ThenPublishesCancelIntent()
        {
            // Arrange
            var modes = CreateModes("classic");
            _catalog.Metadata.Returns(modes);
            _coordinator.TryPublishIntent(WizardIntent.Cancel).Returns(true);

            using var sut = CreateSut();
            sut.Initialize();

            // Act
            sut.RequestCancel();

            // Assert
            _coordinator.Received(1).TryPublishIntent(WizardIntent.Cancel);
        }

        [Test]
        public void WhenInitializeCalledAndCoordinatorReturnsNoSession_ThenVMWorksWithoutSessionSync()
        {
            // Arrange
            var modes = CreateModes("classic");
            _catalog.Metadata.Returns(modes);
            _coordinator.TryGetSession(out Arg.Any<IGameSession>()).Returns(false);

            using var sut = CreateSut();

            // Act
            sut.Initialize();
            sut.SelectMode("classic");

            // Assert
            sut.SelectedGameId.Value.Should().Be("classic");
            sut.CanContinue.CurrentValue.Should().BeTrue();
            _session.UpdateCallCount.Should().Be(0);
        }

        [Test]
        public void WhenCoordinatorNotReadyAndCanContinueTrue_ThenRequestContinueStillPublishesIntent()
        {
            // Arrange
            var modes = CreateModes("classic");
            _catalog.Metadata.Returns(modes);
            _coordinator.TryGetSession(out Arg.Any<IGameSession>()).Returns(false);
            _coordinator.TryPublishIntent(WizardIntent.Continue).Returns(true);

            using var sut = CreateSut();
            sut.Initialize();
            sut.SelectMode("classic");

            // Act
            sut.RequestContinue();

            // Assert
            _coordinator.Received(1).TryPublishIntent(WizardIntent.Continue);
        }
    }
}