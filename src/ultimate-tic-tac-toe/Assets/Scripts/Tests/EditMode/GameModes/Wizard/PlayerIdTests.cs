#nullable enable

using System;
using FluentAssertions;
using NUnit.Framework;
using Runtime.GameModes.Wizard;

namespace Tests.EditMode.GameModes.Wizard
{
    [TestFixture]
    [Category("Unit")]
    public class PlayerIdTests
    {
        [Test]
        public void WhenTryCreateCalledWithValidNumericString_ThenReturnsTrueAndCreatesPlayerId()
        {
            // Arrange / Act
            var result = PlayerId.TryCreate("12345", out var id);

            // Assert
            result.Should().BeTrue();
            id.Should().NotBeNull();
            id!.Value.Should().Be("12345");
        }

        [Test]
        public void WhenTryCreateCalledWithNumericStringWithWhitespace_ThenTrimsAndCreatesPlayerId()
        {
            // Arrange / Act
            var result = PlayerId.TryCreate("  12345  ", out var id);

            // Assert
            result.Should().BeTrue();
            id.Should().NotBeNull();
            id!.Value.Should().Be("12345");
        }

        [Test]
        public void WhenTryCreateCalledWithLeadingZeros_ThenNormalizesToCanonicalForm()
        {
            // Arrange / Act
            var result = PlayerId.TryCreate("000123", out var id);

            // Assert
            result.Should().BeTrue();
            id.Should().NotBeNull();
            id!.Value.Should().Be("123");
        }

        [Test]
        public void WhenTryCreateCalledWithPlusSign_ThenAcceptsAndNormalizesToCanonicalForm()
        {
            // Arrange / Act
            var result = PlayerId.TryCreate("+123", out var id);

            // Assert
            result.Should().BeTrue();
            id.Should().NotBeNull();
            id!.Value.Should().Be("123");
        }

        [Test]
        public void WhenTryCreateCalledWithZero_ThenReturnsTrueAndCreatesPlayerId()
        {
            // Arrange / Act
            var result = PlayerId.TryCreate("0", out var id);

            // Assert
            result.Should().BeTrue();
            id.Should().NotBeNull();
            id!.Value.Should().Be("0");
        }

        [Test]
        public void WhenTryCreateCalledWithMaxUlong_ThenReturnsTrueAndCreatesPlayerId()
        {
            // Arrange
            var maxUlongString = ulong.MaxValue.ToString();

            // Act
            var result = PlayerId.TryCreate(maxUlongString, out var id);

            // Assert
            result.Should().BeTrue();
            id.Should().NotBeNull();
            id!.Value.Should().Be(maxUlongString);
        }

        [Test]
        public void WhenTryCreateCalledWithNull_ThenReturnsFalseAndPlayerIdIsNull()
        {
            // Arrange / Act
            var result = PlayerId.TryCreate(null, out var id);

            // Assert
            result.Should().BeFalse();
            id.Should().BeNull();
        }

        [Test]
        public void WhenTryCreateCalledWithEmptyString_ThenReturnsFalseAndPlayerIdIsNull()
        {
            // Arrange / Act
            var result = PlayerId.TryCreate("", out var id);

            // Assert
            result.Should().BeFalse();
            id.Should().BeNull();
        }

        [Test]
        public void WhenTryCreateCalledWithWhitespaceOnly_ThenReturnsFalseAndPlayerIdIsNull()
        {
            // Arrange / Act
            var result = PlayerId.TryCreate("   ", out var id);

            // Assert
            result.Should().BeFalse();
            id.Should().BeNull();
        }

        [TestCase("abc")]
        [TestCase("12-34")]
        [TestCase("12.34")]
        public void WhenTryCreateCalledWithNonNumericString_ThenReturnsFalseAndPlayerIdIsNull(string input)
        {
            // Arrange / Act
            var result = PlayerId.TryCreate(input, out var id);

            // Assert
            result.Should().BeFalse();
            id.Should().BeNull();
        }

        [Test]
        public void WhenTryCreateCalledWithNegativeNumber_ThenReturnsFalseAndPlayerIdIsNull()
        {
            // Arrange / Act
            var result = PlayerId.TryCreate("-123", out var id);

            // Assert
            result.Should().BeFalse();
            id.Should().BeNull();
        }

        [Test]
        public void WhenTryCreateCalledWithOverflowValue_ThenReturnsFalseAndPlayerIdIsNull()
        {
            // Arrange / Act
            var result = PlayerId.TryCreate("99999999999999999999", out var id);

            // Assert
            result.Should().BeFalse();
            id.Should().BeNull();
        }

        [Test]
        public void WhenConstructorCalledWithInvalidValue_ThenThrowsArgumentException()
        {
            // Arrange
            Action act = () => _ = new PlayerId("invalid");

            // Act / Assert
            act.Should().Throw<ArgumentException>()
                .WithParameterName("value");
        }

        [Test]
        public void WhenToNGOClientIdCalled_ThenReturnsUlong()
        {
            // Arrange
            var id = new PlayerId("12345");

            // Act
            var result = id.ToNGOClientId();

            // Assert
            result.Should().Be(12345UL);
        }

        [Test]
        public void WhenFromNGOCalled_ThenCreatesPlayerId()
        {
            // Arrange / Act
            var id = PlayerId.FromNGO(12345UL);

            // Assert
            id.Should().NotBeNull();
            id.Value.Should().Be("12345");
        }
    }
}

#nullable restore
