using System;
using FluentAssertions;
using NUnit.Framework;
using Runtime.UI.Components;
using UnityEngine.UIElements;

namespace Tests.EditMode.UI.Components
{
    [TestFixture]
    [Category("Unit")]
    public class SegmentedToggleTests
    {
        private SegmentedToggle _toggle;

        [SetUp]
        public void SetUp()
        {
            _toggle = new SegmentedToggle();
        }

        [TearDown]
        public void TearDown()
        {
            _toggle = null;
        }

        [Test]
        public void WhenSetSelectedIndexCalledWithInvalidIndex_ThenThrowsArgumentOutOfRangeException()
        {
            // Arrange
            Action actNegative = () => _toggle.SetSelectedIndex(-1);
            Action actLarge = () => _toggle.SetSelectedIndex(2);

            // Act / Assert
            actNegative.Should().Throw<ArgumentOutOfRangeException>();
            actLarge.Should().Throw<ArgumentOutOfRangeException>();
        }

        [Test]
        public void WhenSetLabelsCalledWithNulls_ThenButtonsHaveEmptyText()
        {
            // Arrange
            _toggle.SetLabels(null, null);

            // Act
            var left = _toggle.Q<Button>("LeftButton");
            var right = _toggle.Q<Button>("RightButton");

            // Assert
            left.text.Should().BeEmpty();
            right.text.Should().BeEmpty();
        }

        [Test]
        public void WhenSetSelectedIndexWithoutNotifyCalled_ThenDoesNotRaiseSelectedIndexChanged()
        {
            // Arrange
            var calls = 0;
            _toggle.SelectedIndexChanged += _ => calls++;

            // Act
            _toggle.SetSelectedIndexWithoutNotify(1);

            // Assert
            calls.Should().Be(0);
            _toggle.SelectedIndex.Should().Be(1);
        }

        [Test]
        public void WhenSetSelectedIndexCalled_ThenRaisesSelectedIndexChangedOnceAndUpdatesSelectedClass()
        {
            // Arrange
            var calls = 0;
            _toggle.SelectedIndexChanged += _ => calls++;

            // Act
            _toggle.SetSelectedIndex(1);

            // Assert
            calls.Should().Be(1);
            _toggle.SelectedIndex.Should().Be(1);

            var left = _toggle.Q<Button>("LeftButton");
            var right = _toggle.Q<Button>("RightButton");

            left.ClassListContains("segmented-toggle__button--selected").Should().BeFalse();
            right.ClassListContains("segmented-toggle__button--selected").Should().BeTrue();
        }

        [Test]
        public void WhenSameIndexSetAgain_ThenIsNoOpAndDoesNotRaiseEvent()
        {
            // Arrange
            var calls = 0;
            _toggle.SelectedIndexChanged += _ => calls++;

            // Act
            _toggle.SetSelectedIndex(1);
            _toggle.SetSelectedIndex(1);

            // Assert
            calls.Should().Be(1);
        }
    }
}