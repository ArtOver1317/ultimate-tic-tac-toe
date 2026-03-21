using System;
using FluentAssertions;
using NUnit.Framework;
using Runtime.GameModes.Wizard.Modes;
using Runtime.UI.GameModes.Wizard;
using UnityEngine.UIElements;

namespace Tests.EditMode.GameModes.Wizard.UI.GameSelection
{
    [TestFixture]
    [Category("Unit")]
    public class GameCardElementTests
    {
        [Test]
        public void WhenBindCalledWithNonNullValues_ThenSetsTextAndTooltipAndSelectedClass()
        {
            // Arrange
            var element = new GameCardElement();

            // Act
            element.Bind("Classic", "3x3 grid", "icon_classic", isSelected: true);

            // Assert
            GetTitle(element).text.Should().Be("Classic");
            GetDescription(element).text.Should().Be("3x3 grid");
            GetIcon(element).tooltip.Should().Be("icon_classic");
            element.ClassListContains(GameCardElement.SelectedClass).Should().BeTrue();
        }

        [Test]
        public void WhenBindCalledWithGameMetadata_ThenDelegatesToStringOverload()
        {
            // Arrange
            var element = new GameCardElement();
           
            var metadata = new GameMetadata(
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
            element.ClassListContains(GameCardElement.SelectedClass).Should().BeTrue();
        }

        [Test]
        public void WhenBindCalledWithNullMetadata_ThenClearsStateAndDeselects()
        {
            // Arrange
            var element = new GameCardElement();

            // Act
            element.Bind(null, isSelected: false);

            // Assert
            GetTitle(element).text.Should().Be(string.Empty);
            GetDescription(element).text.Should().Be(string.Empty);
            GetIcon(element).tooltip.Should().Be(string.Empty);
            element.ClassListContains(GameCardElement.SelectedClass).Should().BeFalse();
        }

        [TestCase(null)]
        public void WhenBindCalledWithNullStrings_ThenSetsEmptyTextAndRemovesSelectedClass(string value)
        {
            // Arrange
            var element = new GameCardElement();

            // Act
            element.Bind(title: value, description: value, iconKey: value, isSelected: false);

            // Assert
            GetTitle(element).text.Should().Be(string.Empty);
            GetDescription(element).text.Should().Be(string.Empty);
            GetIcon(element).tooltip.Should().Be(string.Empty);
            element.ClassListContains(GameCardElement.SelectedClass).Should().BeFalse();
        }

        [Test]
        public void WhenBindCalledWithWhitespaceIconKey_ThenTooltipIsEmpty()
        {
            // Arrange
            var element = new GameCardElement();

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
            var element = new GameCardElement();

            // Act
            element.Bind("A", "B", "iconA", isSelected: true);
            element.Bind("C", "D", "iconB", isSelected: false);

            // Assert
            element.ClassListContains(GameCardElement.SelectedClass).Should().BeFalse();

            // Act
            element.Bind("E", "F", "iconC", isSelected: true);

            // Assert
            element.ClassListContains(GameCardElement.SelectedClass).Should().BeTrue();
        }

        [Test]
        public void WhenConstructed_ThenRootClassIsAppliedAndBindWorks()
        {
            // Arrange
            var element = new GameCardElement();

            // Act
            Action act = () => element.Bind("Test", "Desc", "icon", isSelected: false);

            // Assert
            element.ClassListContains(GameCardElement.RootClass).Should().BeTrue();
            act.Should().NotThrow();
        }

        private static Label GetTitle(GameCardElement element) => element.Q<Label>("Title");

        private static Label GetDescription(GameCardElement element) => element.Q<Label>("Description");

        private static VisualElement GetIcon(GameCardElement element) => element.Q<VisualElement>("Icon");
    }
}