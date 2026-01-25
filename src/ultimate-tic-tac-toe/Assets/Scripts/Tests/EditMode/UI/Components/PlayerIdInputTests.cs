#nullable enable

using System;
using FluentAssertions;
using NUnit.Framework;
using Runtime.UI.Components;
using UnityEngine.UIElements;

namespace Tests.EditMode.UI.Components
{
    [TestFixture]
    [Category("Unit")]
    public class PlayerIdInputTests
    {
        [Test]
        public void WhenCreated_ThenHasExpectedChildElements()
        {
            // Arrange / Act
            var input = new PlayerIdInput();

            // Assert
            input.Q<Label>("TitleLabel").Should().NotBeNull();
            input.Q<TextField>("InputField").Should().NotBeNull();
            input.Q<Label>("ErrorLabel").Should().NotBeNull();
        }

        [Test]
        public void WhenCreated_ThenValueIsEmptyAndErrorIsHidden()
        {
            // Arrange / Act
            var input = new PlayerIdInput();

            // Assert
            input.Value.Should().BeEmpty();
            input.Q<Label>("ErrorLabel").style.display.value.Should().Be(DisplayStyle.None);
        }

        [Test]
        public void WhenSetValueWithoutNotifyCalled_ThenValueChangesButEventNotFired()
        {
            // Arrange
            var input = new PlayerIdInput();
            var eventCount = 0;
            input.ValueChanged += _ => eventCount++;

            // Act
            input.SetValueWithoutNotify("123");

            // Assert
            input.Value.Should().Be("123");
            eventCount.Should().Be(0);
        }

        [Test]
        public void WhenValueChangedNotified_ThenValueChangedEventFires()
        {
            // Arrange
            var input = new PlayerIdInput();
            var lastValue = string.Empty;
            var eventCount = 0;

            input.ValueChanged += value =>
            {
                lastValue = value;
                eventCount++;
            };

            // Act
            input.NotifyValueChangedForTests("123");

            // Assert
            input.Value.Should().Be("123");
            eventCount.Should().Be(1);
            lastValue.Should().Be("123");
        }

        [Test]
        public void WhenSetErrorCalledWithMessage_ThenErrorLabelVisibleWithText()
        {
            // Arrange
            var input = new PlayerIdInput();
            var errorLabel = input.Q<Label>("ErrorLabel");

            // Act
            input.SetError("Invalid ID");

            // Assert
            errorLabel.text.Should().Be("Invalid ID");
            errorLabel.style.display.value.Should().Be(DisplayStyle.Flex);
        }

        [Test]
        public void WhenSetErrorCalledWithNull_ThenErrorLabelHidden()
        {
            // Arrange
            var input = new PlayerIdInput();
            var errorLabel = input.Q<Label>("ErrorLabel");

            input.SetError("Some error");

            // Act
            input.SetError(null);

            // Assert
            errorLabel.style.display.value.Should().Be(DisplayStyle.None);
        }
    }
}

#nullable restore
