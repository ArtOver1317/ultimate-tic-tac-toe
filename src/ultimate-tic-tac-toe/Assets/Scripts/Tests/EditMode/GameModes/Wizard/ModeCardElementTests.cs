using System;
using FluentAssertions;
using NUnit.Framework;
using Runtime.GameModes.Wizard;
using Runtime.UI.GameModes.Wizard;
using UnityEngine.UIElements;

namespace Tests.EditMode.GameModes.Wizard
{
    [TestFixture]
    [Category("Unit")]
    public class ModeCardElementTests
    {
        [Test]
        public void WhenBindCalledWithNonNullValues_ThenSetsTextAndTooltipAndSelectedClass()
        {
            // Arrange
            var element = new ModeCardElement();

            // Act
            element.Bind("Classic", "3x3 grid", "icon_classic", isSelected: true);

            // Assert
            GetTitle(element).text.Should().Be("Classic");
            GetDescription(element).text.Should().Be("3x3 grid");
            GetIcon(element).tooltip.Should().Be("icon_classic");
            element.ClassListContains(ModeCardElement.SelectedClass).Should().BeTrue();
        }

        [Test]
        public void WhenBindCalledWithGameModeMetadata_ThenDelegatesToStringOverload()
        {
            // Arrange
            var element = new ModeCardElement();
            var metadata = new GameModeMetadata(
                id: "classic",
                displayNameKey: "mode.classic",
                descriptionKey: "desc",
                iconAssetKey: "icon",
                sortOrder: 0,
                supportsBot: true,
                supportsOnline: true,
                supportsLocal: true);

            // Act
            element.Bind(metadata, isSelected: true);

            // Assert
            GetTitle(element).text.Should().Be("mode.classic");
            GetDescription(element).text.Should().Be("desc");
            GetIcon(element).tooltip.Should().Be("icon");
            element.ClassListContains(ModeCardElement.SelectedClass).Should().BeTrue();
        }

        [Test]
        public void WhenBindCalledWithNullMetadata_ThenClearsStateAndDeselects()
        {
            // Arrange
            var element = new ModeCardElement();

            // Act
            element.Bind((GameModeMetadata)null, isSelected: false);

            // Assert
            GetTitle(element).text.Should().Be(string.Empty);
            GetDescription(element).text.Should().Be(string.Empty);
            GetIcon(element).tooltip.Should().Be(string.Empty);
            element.ClassListContains(ModeCardElement.SelectedClass).Should().BeFalse();
        }

        [TestCase(null)]
        public void WhenBindCalledWithNullStrings_ThenSetsEmptyTextAndRemovesSelectedClass(string value)
        {
            // Arrange
            var element = new ModeCardElement();

            // Act
            element.Bind(title: value, description: value, iconKey: value, isSelected: false);

            // Assert
            GetTitle(element).text.Should().Be(string.Empty);
            GetDescription(element).text.Should().Be(string.Empty);
            GetIcon(element).tooltip.Should().Be(string.Empty);
            element.ClassListContains(ModeCardElement.SelectedClass).Should().BeFalse();
        }

        [Test]
        public void WhenBindCalledWithWhitespaceIconKey_ThenTooltipIsEmpty()
        {
            // Arrange
            var element = new ModeCardElement();

            // Act
            element.Bind(title: "Title", description: "Desc", iconKey: "   ", isSelected: false);

            // Assert
            GetTitle(element).text.Should().Be("Title");
            GetDescription(element).text.Should().Be("Desc");
            GetIcon(element).tooltip.Should().Be(string.Empty);
        }

        [Test]
        public void WhenBindCalledMultipleTimes_ThenUpdatesStateIdempotently()
        {
            // Arrange
            var element = new ModeCardElement();

            // Act
            element.Bind("A", "B", "iconA", isSelected: true);
            element.Bind("C", "D", "iconB", isSelected: false);

            // Assert
            element.ClassListContains(ModeCardElement.SelectedClass).Should().BeFalse();

            // Act
            element.Bind("E", "F", "iconC", isSelected: true);

            // Assert
            element.ClassListContains(ModeCardElement.SelectedClass).Should().BeTrue();
        }

        [Test]
        public void WhenConstructed_ThenRootClassIsAppliedAndBindWorks()
        {
            // Arrange
            var element = new ModeCardElement();

            // Act
            Action act = () => element.Bind("Test", "Desc", "icon", isSelected: false);

            // Assert
            element.ClassListContains(ModeCardElement.RootClass).Should().BeTrue();
            act.Should().NotThrow();
        }

        private static Label GetTitle(ModeCardElement element) => element.Q<Label>("Title");

        private static Label GetDescription(ModeCardElement element) => element.Q<Label>("Description");

        private static VisualElement GetIcon(ModeCardElement element) => element.Q<VisualElement>("Icon");
    }
}
