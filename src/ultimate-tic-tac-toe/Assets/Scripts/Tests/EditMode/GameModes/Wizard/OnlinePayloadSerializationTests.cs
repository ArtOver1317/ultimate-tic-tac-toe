#nullable enable

using System;
using System.Text;
using FluentAssertions;
using NUnit.Framework;
using Runtime.GameModes.Wizard;

namespace Tests.EditMode.GameModes.Wizard
{
    [TestFixture]
    [Category("Unit")]
    public class OnlinePayloadSerializationTests
    {
        [Test]
        public void WhenCountdownPayloadMalformed_ThenTryDeserializeCountdownTargetReturnsFalse()
        {
            // Arrange
            var payload = new byte[] { 0xFF, 0x00 };

            // Act
            var ok = OnlinePayloadSerialization.TryDeserializeCountdownTarget(payload, out var target);

            // Assert
            ok.Should().BeFalse();
            target.Should().Be(0d);
        }

        [Test]
        public void WhenCountdownPayloadValid_ThenTryDeserializeCountdownTargetReturnsParsedValue()
        {
            // Arrange
            var payload = Encoding.UTF8.GetBytes("T|103.25");

            // Act
            var ok = OnlinePayloadSerialization.TryDeserializeCountdownTarget(payload, out var target);

            // Assert
            ok.Should().BeTrue();
            target.Should().BeApproximately(103.25d, 0.000001d);
        }

        [Test]
        public void WhenSerializeCountdownTargetAndDeserialize_ThenRoundTripPreservesValue()
        {
            // Arrange
            const double expected = 245.875d;
            var payload = OnlinePayloadSerialization.SerializeCountdownTarget(expected);

            // Act
            var ok = OnlinePayloadSerialization.TryDeserializeCountdownTarget(payload, out var actual);

            // Assert
            ok.Should().BeTrue();
            actual.Should().BeApproximately(expected, 0.000001d);
        }

        [Test]
        public void WhenMatchConfigPayloadMalformed_ThenTryDeserializeMatchConfigReturnsFalse()
        {
            // Arrange
            var payload = Encoding.UTF8.GetBytes("wrong-payload");

            // Act
            var ok = OnlinePayloadSerialization.TryDeserializeMatchConfig(payload, out var config);

            // Assert
            ok.Should().BeFalse();
            config.Should().Be(default(OnlineMatchConfigPayload));
        }

        [Test]
        public void WhenMatchConfigPayloadValid_ThenTryDeserializeMatchConfigReturnsPayload()
        {
            // Arrange
            var payload = Encoding.UTF8.GetBytes("C|tic-tac-toe|5|1");

            // Act
            var ok = OnlinePayloadSerialization.TryDeserializeMatchConfig(payload, out var config);

            // Assert
            ok.Should().BeTrue();
            config.GameId.Should().Be("tic-tac-toe");
            config.BoardSize.Should().Be(5);
            config.IsUltimate.Should().BeTrue();
        }

        [Test]
        public void WhenSerializeMatchConfigAndDeserialize_ThenRoundTripPreservesFields()
        {
            // Arrange
            var expected = new OnlineMatchConfigPayload("tic-tac-toe", 3, isUltimate: false);
            var payload = OnlinePayloadSerialization.SerializeMatchConfig(expected);

            // Act
            var ok = OnlinePayloadSerialization.TryDeserializeMatchConfig(payload, out var actual);

            // Assert
            ok.Should().BeTrue();
            actual.GameId.Should().Be(expected.GameId);
            actual.BoardSize.Should().Be(expected.BoardSize);
            actual.IsUltimate.Should().Be(expected.IsUltimate);
        }

    }
}

#nullable restore
