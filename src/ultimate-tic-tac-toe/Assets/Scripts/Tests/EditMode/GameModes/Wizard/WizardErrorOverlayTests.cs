using FluentAssertions;
using NUnit.Framework;
using Runtime.UI.Components;
using UnityEngine.UIElements;

namespace Tests.EditMode.GameModes.Wizard
{
    [TestFixture]
    [Category("Unit")]
    public class WizardErrorOverlayTests
    {
        [Test]
        public void WhenPresentCalledWithToastPresentation_ThenToastShownAndModalHidden()
        {
            // Arrange
            var overlay = new WizardErrorOverlay();
            var presentation = new UIErrorPresentation("code", "message", false, UIErrorDisplayType.Toast);

            // Act
            overlay.Present(presentation);

            // Assert
            overlay.style.display.value.Should().Be(DisplayStyle.Flex);
            overlay.Q<WizardToast>("WizardToast").IsVisible.Should().BeTrue();
            overlay.Q<WizardModal>("WizardModal").IsVisible.Should().BeFalse();
        }

        [Test]
        public void WhenPresentCalledWithModalPresentation_ThenModalShownAndToastHidden()
        {
            // Arrange
            var overlay = new WizardErrorOverlay();
            var presentation = new UIErrorPresentation("code", "message", true, UIErrorDisplayType.Modal);

            // Act
            overlay.Present(presentation);

            // Assert
            overlay.style.display.value.Should().Be(DisplayStyle.Flex);
            overlay.Q<WizardModal>("WizardModal").IsVisible.Should().BeTrue();
            overlay.Q<WizardToast>("WizardToast").IsVisible.Should().BeFalse();
        }

        [Test]
        public void WhenPresentCalledWithInlinePresentation_ThenOverlayReset()
        {
            // Arrange
            var overlay = new WizardErrorOverlay();
            var presentation = new UIErrorPresentation("code", "message", false, UIErrorDisplayType.Inline);

            // Act
            overlay.Present(presentation);

            // Assert
            overlay.style.display.value.Should().Be(DisplayStyle.None);
            overlay.IsBlocking.Should().BeFalse();
            overlay.Q<WizardToast>("WizardToast").IsVisible.Should().BeFalse();
            overlay.Q<WizardModal>("WizardModal").IsVisible.Should().BeFalse();
        }

        [Test]
        public void WhenPresentCalledWithNullPresentation_ThenOverlayReset()
        {
            // Arrange
            var overlay = new WizardErrorOverlay();

            // Act
            overlay.Present(null);

            // Assert
            overlay.style.display.value.Should().Be(DisplayStyle.None);
            overlay.IsBlocking.Should().BeFalse();
            overlay.Q<WizardToast>("WizardToast").IsVisible.Should().BeFalse();
            overlay.Q<WizardModal>("WizardModal").IsVisible.Should().BeFalse();
        }

        [Test]
        public void WhenPresentCalledWithBlockingModal_ThenIsBlockingIsTrueAndPickingModeIsPosition()
        {
            // Arrange
            var overlay = new WizardErrorOverlay();
            var presentation = new UIErrorPresentation("code", "message", true, UIErrorDisplayType.Modal);

            // Act
            overlay.Present(presentation);

            // Assert
            overlay.IsBlocking.Should().BeTrue();
            overlay.pickingMode.Should().Be(PickingMode.Position);
        }

        [Test]
        public void WhenOverlayIsPresentingToast_ThenIsBlockingFlagDoesNotAffectPickingMode()
        {
            // Arrange
            var overlay = new WizardErrorOverlay();
            var presentation = new UIErrorPresentation("code", "message", true, UIErrorDisplayType.Toast);

            // Act
            overlay.Present(presentation);

            // Assert
            overlay.IsBlocking.Should().BeTrue();
            overlay.pickingMode.Should().Be(PickingMode.Ignore);
        }

        [Test]
        public void WhenResetStateCalled_ThenOverlayHiddenAndIsBlockingIsFalse()
        {
            // Arrange
            var overlay = new WizardErrorOverlay();
            overlay.Present(new UIErrorPresentation("code", "message", true, UIErrorDisplayType.Modal));

            // Act
            overlay.ResetState();

            // Assert
            overlay.style.display.value.Should().Be(DisplayStyle.None);
            overlay.IsBlocking.Should().BeFalse();
            overlay.pickingMode.Should().Be(PickingMode.Ignore);
            overlay.Q<WizardToast>("WizardToast").IsVisible.Should().BeFalse();
            overlay.Q<WizardModal>("WizardModal").IsVisible.Should().BeFalse();
        }
    }
}