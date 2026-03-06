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
            var payload = Encoding.UTF8.GetBytes("C|tic-tac-toe|5|1|20");

            // Act
            var ok = OnlinePayloadSerialization.TryDeserializeMatchConfig(payload, out var config);

            // Assert
            ok.Should().BeTrue();
            config.GameId.Should().Be("tic-tac-toe");
            config.BoardSize.Should().Be(5);
            config.IsUltimate.Should().BeTrue();
            config.MoveTimeLimitSeconds.Should().Be(20);
        }

        [Test]
        public void WhenMatchConfigPayloadWithoutMoveTimer_ThenTryDeserializeMatchConfigReturnsZeroTimer()
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
            config.MoveTimeLimitSeconds.Should().Be(0);
        }

        [Test]
        public void WhenSerializeMatchConfigAndDeserialize_ThenRoundTripPreservesFields()
        {
            // Arrange
            var expected = new OnlineMatchConfigPayload("tic-tac-toe", 3, isUltimate: false, moveTimeLimitSeconds: 15);
            var payload = OnlinePayloadSerialization.SerializeMatchConfig(expected);

            // Act
            var ok = OnlinePayloadSerialization.TryDeserializeMatchConfig(payload, out var actual);

            // Assert
            ok.Should().BeTrue();
            actual.GameId.Should().Be(expected.GameId);
            actual.BoardSize.Should().Be(expected.BoardSize);
            actual.IsUltimate.Should().Be(expected.IsUltimate);
            actual.MoveTimeLimitSeconds.Should().Be(expected.MoveTimeLimitSeconds);
            actual.PlacementTimeLimitSeconds.Should().Be(expected.PlacementTimeLimitSeconds);
        }

        [Test]
        public void WhenBattleshipMatchConfigPayloadIncludesPlacementTimer_ThenTryDeserializeReturnsPlacementTimer()
        {
            // Arrange
            var payload = Encoding.UTF8.GetBytes("C|battleship|10|0|30|45");

            // Act
            var ok = OnlinePayloadSerialization.TryDeserializeMatchConfig(payload, out var config);

            // Assert
            ok.Should().BeTrue();
            config.GameId.Should().Be("battleship");
            config.BoardSize.Should().Be(10);
            config.IsUltimate.Should().BeFalse();
            config.MoveTimeLimitSeconds.Should().Be(30);
            config.PlacementTimeLimitSeconds.Should().Be(45);
        }

        [Test]
        public void WhenSerializePlayerNamePayloadWithCustomNameAndDeserialize_ThenRoundTripPreservesFields()
        {
            var payload = OnlinePlayerNamePayload.Serialize(isHost: true, customName: "Alice");

            var ok = OnlinePlayerNamePayload.TryDeserialize(payload, out var actual);

            ok.Should().BeTrue();
            actual.IsHost.Should().BeTrue();
            actual.CustomName.Should().Be("Alice");
        }

        [Test]
        public void WhenSerializePlayerNamePayloadWithoutCustomName_ThenDeserializeReturnsNullCustomName()
        {
            var payload = OnlinePlayerNamePayload.Serialize(isHost: false, customName: null);

            var ok = OnlinePlayerNamePayload.TryDeserialize(payload, out var actual);

            ok.Should().BeTrue();
            actual.IsHost.Should().BeFalse();
            actual.CustomName.Should().BeNull();
        }

        [Test]
        public void WhenPlayerNamePayloadHasInvalidName_ThenTryDeserializeReturnsFalse()
        {
            var payload = Encoding.UTF8.GetBytes("N|1|H|1|Bad#Name");

            var ok = OnlinePlayerNamePayload.TryDeserialize(payload, out var actual);

            ok.Should().BeFalse();
            actual.Should().Be(default(OnlinePlayerNamePayloadData));
        }

        [Test]
        public void WhenSerializePlayerNamePayloadContainsProtocolSeparator_ThenThrowsArgumentException()
        {
            System.Func<byte[]> action = () => OnlinePlayerNamePayload.Serialize(isHost: true, customName: "Bad|Name");

            action.Should().Throw<ArgumentException>();
        }

        [Test]
        public void WhenSerializePlayerNamePayloadDoesNotPassValidator_ThenThrowsArgumentException()
        {
            System.Func<byte[]> action = () => OnlinePlayerNamePayload.Serialize(isHost: true, customName: "Bad Name");

            action.Should().Throw<ArgumentException>();
        }

        [Test]
        public void WhenDeserializeWithWrongTypeMarker_ThenReturnsFalse()
        {
            var payload = Encoding.UTF8.GetBytes("X|1|H|1|Alice");

            var ok = OnlinePlayerNamePayload.TryDeserialize(payload, out var actual);

            ok.Should().BeFalse();
            actual.Should().Be(default(OnlinePlayerNamePayloadData));
        }

        [Test]
        public void WhenDeserializeWithUnknownVersion_ThenReturnsFalse()
        {
            var payload = Encoding.UTF8.GetBytes("N|99|H|1|Alice");

            var ok = OnlinePlayerNamePayload.TryDeserialize(payload, out var actual);

            ok.Should().BeFalse();
            actual.Should().Be(default(OnlinePlayerNamePayloadData));
        }

        [Test]
        public void WhenDeserializeWithUnknownRoleMarker_ThenReturnsFalse()
        {
            var payload = Encoding.UTF8.GetBytes("N|1|X|1|Alice");

            var ok = OnlinePlayerNamePayload.TryDeserialize(payload, out var actual);

            ok.Should().BeFalse();
            actual.Should().Be(default(OnlinePlayerNamePayloadData));
        }

        [Test]
        public void WhenDeserializeWithHasCustomZeroButNonEmptyName_ThenReturnsFalse()
        {
            var payload = Encoding.UTF8.GetBytes("N|1|H|0|Alice");

            var ok = OnlinePlayerNamePayload.TryDeserialize(payload, out var actual);

            ok.Should().BeFalse();
            actual.Should().Be(default(OnlinePlayerNamePayloadData));
        }

        [Test]
        public void WhenDeserializeWithWrongPartCount_ThenReturnsFalse()
        {
            var invalidPayloads = new[]
            {
                Encoding.UTF8.GetBytes("N|1|H|1"),
                Encoding.UTF8.GetBytes("garbage"),
                Encoding.UTF8.GetBytes("N|1|H|1|Bad|Name"),
                Array.Empty<byte>()
            };

            foreach (var payload in invalidPayloads)
            {
                var ok = OnlinePlayerNamePayload.TryDeserialize(payload, out var actual);
                var payloadText = payload.Length == 0 ? "<empty>" : Encoding.UTF8.GetString(payload);

                ok.Should().BeFalse($"payload '{payloadText}' must be rejected");
                actual.Should().Be(default(OnlinePlayerNamePayloadData), $"payload '{payloadText}' must not produce parsed data");
            }
        }

        [Test]
        public void WhenDeserializeWithHasCustomOneButEmptyName_ThenReturnsFalse()
        {
            var payload = Encoding.UTF8.GetBytes("N|1|H|1|");

            var ok = OnlinePlayerNamePayload.TryDeserialize(payload, out var actual);

            ok.Should().BeFalse();
            actual.Should().Be(default(OnlinePlayerNamePayloadData));
        }

        [Test]
        public void WhenDeserializeWithUnsupportedHasCustomMarker_ThenReturnsFalse()
        {
            var payload = Encoding.UTF8.GetBytes("N|1|H|2|Alice");

            var ok = OnlinePlayerNamePayload.TryDeserialize(payload, out var actual);

            ok.Should().BeFalse();
            actual.Should().Be(default(OnlinePlayerNamePayloadData));
        }

    }
}

#nullable restore
