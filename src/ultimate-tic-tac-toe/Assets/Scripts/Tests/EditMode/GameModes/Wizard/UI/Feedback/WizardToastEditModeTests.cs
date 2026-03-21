using System;
using FluentAssertions;
using NUnit.Framework;
using Runtime.UI.Components;
using UnityEngine.UIElements;

namespace Tests.EditMode.GameModes.Wizard.UI.Feedback
{
    [TestFixture]
    [Category("Unit")]
    public class WizardToastEditModeTests
    {
        [Test]
        public void WhenShowCalled_ThenToastBecomesVisibleWithMessage()
        {
            // Arrange
            var toast = new WizardToast();

            // Act
            toast.Show("Hello");

            // Assert
            toast.IsVisible.Should().BeTrue();
            toast.style.display.value.Should().Be(DisplayStyle.Flex);
            toast.Q<Label>("ToastMessage").text.Should().Be("Hello");
        }

        [Test]
        public void WhenHideCalled_ThenToastBecomesHidden()
        {
            // Arrange
            var toast = new WizardToast();
            toast.Show("Hello");

            // Act
            toast.Hide();

            // Assert
            toast.IsVisible.Should().BeFalse();
            toast.style.display.value.Should().Be(DisplayStyle.None);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void WhenShowCalledWithZeroOrNullAutoHide_ThenToastRemainsVisibleUntilManualHide(bool useNull)
        {
            // Arrange
            var toast = new WizardToast();

            // Act
            var autoHide = useNull ? (TimeSpan?)null : TimeSpan.Zero;
            toast.Show("Hello", autoHide);

            // Assert
            toast.IsVisible.Should().BeTrue();
        }
    }
}
