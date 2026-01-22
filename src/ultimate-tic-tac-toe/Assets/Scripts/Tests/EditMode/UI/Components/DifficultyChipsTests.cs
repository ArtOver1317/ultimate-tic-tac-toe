using System;
using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using Runtime.UI.Components;
using UnityEngine.UIElements;

namespace Tests.EditMode.UI.Components
{
    [TestFixture]
    [Category("Unit")]
    public class DifficultyChipsTests
    {
        private DifficultyChips _chips;

        [SetUp]
        public void SetUp() => _chips = new DifficultyChips();

        [TearDown]
        public void TearDown() => _chips = null;

        [Test]
        public void WhenSetItemsCalledWithNull_ThenClearsButtonsAndSelectedIdIsNull()
        {
            // Arrange
            _chips.SetItems(CreateItems(("Easy", "Easy")));
            _chips.SetSelectedId("Easy");
            _chips.SelectedId.Should().Be("Easy");

            // Act
            _chips.SetItems(null);

            // Assert
            _chips.childCount.Should().Be(0);
            _chips.SelectedId.Should().BeNull();
        }

        [Test]
        public void WhenSetItemsCalledWithEmptyList_ThenClearsButtonsAndSelectedIdIsNull()
        {
            // Arrange
            _chips.SetItems(CreateItems(("Easy", "Easy")));
            _chips.SetSelectedId("Easy");
            _chips.SelectedId.Should().Be("Easy");

            // Act
            _chips.SetItems(Array.Empty<DifficultyChipItem>());

            // Assert
            _chips.childCount.Should().Be(0);
            _chips.SelectedId.Should().BeNull();
        }

        [Test]
        public void WhenSetItemsCalledWithValidItems_ThenCreatesButtonsWithCorrectLabels()
        {
            // Arrange
            var items = CreateItems(("Easy", "Easy"), ("Normal", "Normal"), ("Hard", "Hard"));

            // Act
            _chips.SetItems(items);

            // Assert
            _chips.childCount.Should().Be(3);
            _chips.Q<Button>("Easy").text.Should().Be("Easy");
            _chips.Q<Button>("Normal").text.Should().Be("Normal");
            _chips.Q<Button>("Hard").text.Should().Be("Hard");
        }

        [Test]
        public void WhenSetItemsCalledAndPreviouslySelectedIdIsNotInNewItems_ThenSelectedIdIsCleared()
        {
            // Arrange
            _chips.SetItems(CreateItems(("A", "A"), ("B", "B"), ("C", "C")));
            _chips.SetSelectedId("B");

            // Act
            _chips.SetItems(CreateItems(("X", "X"), ("Y", "Y")));

            // Assert
            _chips.SelectedId.Should().BeNull();
        }

        [Test]
        public void WhenSetItemsCalledWithDuplicateIds_ThenThrowsInvalidOperationException()
        {
            // Arrange
            var items = new[]
            {
                new DifficultyChipItem("A", "A"),
                new DifficultyChipItem("A", "A2")
            };

            // Act
            Action act = () => _chips.SetItems(items);

            // Assert
            act.Should().Throw<InvalidOperationException>();
        }

        [Test]
        public void WhenSetItemsCalledWithNullItemInList_ThenThrowsArgumentException()
        {
            // Arrange
#pragma warning disable CS8625
            var items = new DifficultyChipItem[]
            {
                new("A", "A"),
                null
            };
#pragma warning restore CS8625

            // Act
            Action act = () => _chips.SetItems(items);

            // Assert
            act.Should().Throw<ArgumentException>();
        }

        [Test]
        public void WhenSetSelectedIdCalledWithValidId_ThenUpdatesSelectedIdAndRaisesEvent()
        {
            // Arrange
            _chips.SetItems(CreateItems(("Easy", "Easy"), ("Normal", "Normal")));
            var calls = 0;
            _chips.SelectedIdChanged += _ => calls++;

            // Act
            _chips.SetSelectedId("Normal");

            // Assert
            _chips.SelectedId.Should().Be("Normal");
            calls.Should().Be(1);
            _chips.Q<Button>("Normal").ClassListContains("difficulty-chips__item--selected").Should().BeTrue();
        }

        [Test]
        public void WhenSetSelectedIdCalledWithUnknownId_ThenNormalizesToNullAndNoEvent()
        {
            // Arrange
            _chips.SetItems(CreateItems(("Easy", "Easy"), ("Normal", "Normal")));
            var calls = 0;
            _chips.SelectedIdChanged += _ => calls++;

            // Act
            _chips.SetSelectedId("Unknown");

            // Assert
            _chips.SelectedId.Should().BeNull();
            calls.Should().Be(0);
        }

        [Test]
        public void WhenSetSelectedIdWithoutNotifyCalled_ThenUpdatesSelectedIdButDoesNotRaiseEvent()
        {
            // Arrange
            _chips.SetItems(CreateItems(("Easy", "Easy"), ("Hard", "Hard")));
            var calls = 0;
            _chips.SelectedIdChanged += _ => calls++;

            // Act
            _chips.SetSelectedIdWithoutNotify("Hard");

            // Assert
            _chips.SelectedId.Should().Be("Hard");
            calls.Should().Be(0);
        }

        [Test]
        public void WhenSameIdSetAgain_ThenIsNoOpAndDoesNotRaiseEvent()
        {
            // Arrange
            _chips.SetItems(CreateItems(("Easy", "Easy"), ("Hard", "Hard")));
            var calls = 0;
            _chips.SelectedIdChanged += _ => calls++;

            // Act
            _chips.SetSelectedId("Easy");
            _chips.SetSelectedId("Easy");

            // Assert
            calls.Should().Be(1);
        }

        [Test]
        public void WhenSetLabelCalledForExistingId_ThenUpdatesButtonText()
        {
            // Arrange
            _chips.SetItems(CreateItems(("Easy", "Easy"), ("Hard", "Hard")));

            // Act
            _chips.SetLabel("Easy", "Лёгкий");

            // Assert
            _chips.Q<Button>("Easy").text.Should().Be("Лёгкий");
        }

        [Test]
        public void WhenSetLabelCalledForUnknownId_ThenIsNoOp()
        {
            // Arrange
            _chips.SetItems(CreateItems(("Easy", "Easy"), ("Hard", "Hard")));
            var before = _chips.Q<Button>("Easy").text;

            // Act
            _chips.SetLabel("Unknown", "Text");

            // Assert
            _chips.Q<Button>("Easy").text.Should().Be(before);
        }

        private static IReadOnlyList<DifficultyChipItem> CreateItems(params (string id, string label)[] items)
        {
            var result = new DifficultyChipItem[items.Length];
            for (var i = 0; i < items.Length; i++)
                result[i] = new DifficultyChipItem(items[i].id, items[i].label);

            return Array.AsReadOnly(result);
        }
    }
}
