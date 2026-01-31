using FluentAssertions;
using NUnit.Framework;
using Runtime.UI.Components;
using UnityEngine.UIElements;

namespace Tests.EditMode.GameModes.Wizard
{
    [TestFixture]
    [Category("Unit")]
    public class WizardModalEditModeTests
    {
        [Test]
        public void WhenShowCalled_ThenModalBecomesVisibleWithMessage()
        {
            // Arrange
            var modal = new WizardModal();

            // Act
            modal.Show("Error");

            // Assert
            modal.IsVisible.Should().BeTrue();
            modal.style.display.value.Should().Be(DisplayStyle.Flex);
            modal.Q<Label>("ModalMessage").text.Should().Be("Error");
            modal.pickingMode.Should().Be(PickingMode.Position);
        }

        [Test]
        public void WhenHideCalled_ThenModalBecomesHidden()
        {
            // Arrange
            var modal = new WizardModal();
            modal.Show("Error");

            // Act
            modal.Hide();

            // Assert
            modal.IsVisible.Should().BeFalse();
            modal.style.display.value.Should().Be(DisplayStyle.None);
            modal.pickingMode.Should().Be(PickingMode.Ignore);
        }

        [Test]
        public void WhenSetButtonTextCalled_ThenButtonTextUpdates()
        {
            // Arrange
            var modal = new WizardModal();

            // Act
            modal.SetButtonText("Confirm");

            // Assert
            modal.Q<Button>("OkButton").text.Should().Be("Confirm");
        }

        [Test]
        public void WhenHideCalledMultipleTimes_ThenIsIdempotent()
        {
            // Arrange
            var modal = new WizardModal();
            modal.Show("Error");

            // Act
            modal.Hide();
            modal.Hide();

            // Assert
            modal.IsVisible.Should().BeFalse();
        }
    }
}