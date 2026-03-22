using System;
using FluentAssertions;
using NUnit.Framework;
using Runtime.UI.Components;
using UnityEngine.UIElements;

namespace Tests.EditMode.UI.Components
{
    [TestFixture]
    [Category("Unit")]
    public class MatchmakingTimerTests
    {
        private MatchmakingTimer _timer;

        [SetUp]
        public void SetUp() => _timer = new MatchmakingTimer();

        [TearDown]
        public void TearDown() => _timer = null;

        [Test]
        public void WhenSetTimeCalled_ThenFormatsTimeAsMMSS()
        {
            // Arrange
            var label = _timer.Q<Label>("TimerLabel");

            // Act
            _timer.SetTime(TimeSpan.FromSeconds(125));

            // Assert
            label.text.Should().Be("02:05");
        }

        [Test]
        public void WhenSetPrefixCalled_ThenUpdatesLabelWithPrefix()
        {
            // Arrange
            var label = _timer.Q<Label>("TimerLabel");

            // Act
            _timer.SetPrefix("Searching");
            _timer.SetTime(TimeSpan.FromSeconds(7));

            // Assert
            label.text.Should().Be("Searching 00:07...");
        }

        [Test]
        public void WhenSetTimeCalledWithZero_ThenShowsZeroZero()
        {
            // Arrange
            var label = _timer.Q<Label>("TimerLabel");

            // Act
            _timer.SetTime(TimeSpan.Zero);

            // Assert
            label.text.Should().Be("00:00");
        }

        [Test]
        public void WhenSetTimeCalledWithNegativeTime_ThenClampsToZero()
        {
            // Arrange
            var label = _timer.Q<Label>("TimerLabel");

            // Act
            _timer.SetTime(TimeSpan.FromSeconds(-5));

            // Assert
            label.text.Should().Be("00:00");
        }
    }
}