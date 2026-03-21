#nullable enable

using System;
using FluentAssertions;
using NUnit.Framework;
using Runtime.Games.Battleship.Core;
using Runtime.Games.Battleship.Placement;

namespace Tests.EditMode.Games.Battleship.Placement
{
    [TestFixture]
    [Category("Unit")]
    public sealed class BattleshipLayoutSerializerTests
    {
        [Test]
        public void WhenLayoutSerializedAndDeserialized_ThenRoundTripPreservesPayload()
        {
            var serializer = new BattleshipLayoutSerializer();
            var validator = new BattleshipPlacementValidator();
            var autoPlacer = new BattleshipAutoPlacer(validator);
            var layout = autoPlacer.Generate(13579);

            var payload = serializer.Serialize(layout);
            var ok = serializer.TryDeserialize(payload, out var parsedLayout);

            ok.Should().BeTrue();
            parsedLayout.IsInitialized.Should().BeTrue();
            serializer.Serialize(parsedLayout).Should().Be(payload);
        }

        [Test]
        public void WhenLayoutPayloadHasUnknownVersion_ThenTryDeserializeReturnsFalse()
        {
            var serializer = new BattleshipLayoutSerializer();

            var ok = serializer.TryDeserialize("v9:invalid", out var parsedLayout);

            ok.Should().BeFalse();
            parsedLayout.IsInitialized.Should().BeFalse();
        }

        [Test]
        public void WhenSerializerSerializesSameFleetWithDifferentShipOrder_ThenPayloadIsCanonical()
        {
            var validator = new BattleshipPlacementValidator();
            var autoPlacer = new BattleshipAutoPlacer(validator);
            var serializer = new BattleshipLayoutSerializer();
            var originalLayout = autoPlacer.Generate(97531);
            var reversedShips = new ShipPlacement[FleetLayout.ExpectedShipCount];

            for (var i = 0; i < FleetLayout.ExpectedShipCount; i++)
            {
                reversedShips[i] = originalLayout.Ships![FleetLayout.ExpectedShipCount - 1 - i];
            }

            var reversedLayout = new FleetLayout(Array.AsReadOnly(reversedShips));

            serializer.Serialize(originalLayout).Should().Be(serializer.Serialize(reversedLayout));
        }
    }
}