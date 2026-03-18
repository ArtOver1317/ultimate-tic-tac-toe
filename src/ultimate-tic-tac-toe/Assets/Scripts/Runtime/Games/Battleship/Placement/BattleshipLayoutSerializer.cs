#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Runtime.Games.Battleship.Core;
using Runtime.Games.Battleship.ECS.Core;
using Runtime.Games.Battleship.Networking;
using Runtime.Gameplay;

namespace Runtime.Games.Battleship.Placement
{
    public sealed class BattleshipLayoutSerializer : IBattleshipLayoutSerializer
    {
        private const string _versionPrefix = "v1:";
        private const int _estimatedSerializedLayoutCapacity = 96;
        private const int _serializedShipPartCount = 3;
        private const int _shipSizeBucketCount = (int)ShipSize.Four + 1;

        public string Serialize(FleetLayout layout)
        {
            if (!layout.IsInitialized || layout.Ships == null)
                throw new ArgumentException("Fleet layout is not initialized.", nameof(layout));

            var orderedShips = BuildCanonicalOrder(layout.Ships);
            var builder = new StringBuilder(capacity: _estimatedSerializedLayoutCapacity);
            builder.Append(_versionPrefix);

            for (var i = 0; i < orderedShips.Count; i++)
            {
                AppendSerializedShip(builder, orderedShips[i], i > 0);
            }

            return builder.ToString();
        }

        public bool TryDeserialize(string payload, out FleetLayout layout)
        {
            layout = default;

            if (!TryGetSerializedShips(payload, out var shipsRaw))
                return false;

            return TryDeserializeShips(shipsRaw, out var ships) && TryCreateLayout(ships, out layout);
        }

        private static void AppendSerializedShip(StringBuilder builder, in ShipPlacement ship, bool includeSeparator)
        {
            if (includeSeparator)
                builder.Append(';');

            if (!TryGetStartIndex(ship.StartCell, out var startCellIndex))
                throw new ArgumentException("Ship start cell is out of bounds.", nameof(ship));

            builder.Append(((int)ship.Size).ToString(CultureInfo.InvariantCulture));
            builder.Append(',');
            builder.Append(ship.Orientation == ShipOrientation.Horizontal ? 'H' : 'V');
            builder.Append(',');
            builder.Append(startCellIndex.ToString(CultureInfo.InvariantCulture));
        }

        private static bool TryGetSerializedShips(string payload, out string[] shipsRaw)
        {
            shipsRaw = Array.Empty<string>();

            if (string.IsNullOrWhiteSpace(payload) || !payload.StartsWith(_versionPrefix, StringComparison.Ordinal))
                return false;

            shipsRaw = payload[_versionPrefix.Length..].Split(';');
            return shipsRaw.Length == FleetLayout.ExpectedShipCount;
        }

        private static bool TryDeserializeShips(string[] shipsRaw, out ShipPlacement[] ships)
        {
            ships = new ShipPlacement[FleetLayout.ExpectedShipCount];

            for (var i = 0; i < shipsRaw.Length; i++)
            {
                if (!TryDeserializeShip(shipsRaw[i], BattleshipFleetConfig.StandardFleetOrder[i], out ships[i]))
                    return false;
            }

            return true;
        }

        private static bool TryDeserializeShip(string raw, ShipSize expectedSize, out ShipPlacement ship)
        {
            ship = default;
            var parts = raw.Split(',');
            
            if (parts.Length != _serializedShipPartCount
                || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var sizeValue)
                || sizeValue != (int)expectedSize
                || !TryParseOrientation(parts[1], out var orientation)
                || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var startCellIndex)
                || !TryGetStartCell(startCellIndex, out var startCell))
                return false;

            ship = new ShipPlacement(expectedSize, orientation, startCell);
            return true;
        }

        private static bool TryCreateLayout(ShipPlacement[] ships, out FleetLayout layout)
        {
            try
            {
                layout = new FleetLayout(Array.AsReadOnly(ships));
                return true;
            }
            catch
            {
                layout = default;
                return false;
            }
        }

        private static IReadOnlyList<ShipPlacement> BuildCanonicalOrder(IReadOnlyList<ShipPlacement> source)
        {
            if (source.Count != FleetLayout.ExpectedShipCount)
                throw new ArgumentException($"Fleet must contain exactly {FleetLayout.ExpectedShipCount} ships.", nameof(source));

            var grouped = CreateShipBuckets();
            AddShipsToBuckets(source, grouped);
            SortShipBuckets(grouped);
            return BuildCanonicalFleet(grouped, source);
        }

        private static List<ShipPlacement>[] CreateShipBuckets()
        {
            var grouped = new List<ShipPlacement>[_shipSizeBucketCount];
            
            for (var i = 0; i < grouped.Length; i++)
            {
                grouped[i] = new List<ShipPlacement>();
            }

            return grouped;
        }

        private static void AddShipsToBuckets(IReadOnlyList<ShipPlacement> source, List<ShipPlacement>[] grouped)
        {
            for (var i = 0; i < source.Count; i++)
            {
                var ship = source[i];
                var size = (int)ship.Size;
                
                if (size is < (int)ShipSize.One or > (int)ShipSize.Four)
                    throw new ArgumentException("Fleet contains unsupported ship size.", nameof(source));

                grouped[size].Add(ship);
            }
        }

        private static void SortShipBuckets(List<ShipPlacement>[] grouped)
        {
            for (var i = (int)ShipSize.One; i <= (int)ShipSize.Four; i++)
            {
                grouped[i].Sort(CompareShipPlacements);
            }
        }

        private static int CompareShipPlacements(ShipPlacement left, ShipPlacement right)
        {
            var leftIndex = left.StartCell.Major * BattleshipEcsBoard.DefaultBoardSize + left.StartCell.Minor;
            var rightIndex = right.StartCell.Major * BattleshipEcsBoard.DefaultBoardSize + right.StartCell.Minor;
            var byIndex = leftIndex.CompareTo(rightIndex);
            return byIndex != 0 ? byIndex : left.Orientation.CompareTo(right.Orientation);
        }

        private static IReadOnlyList<ShipPlacement> BuildCanonicalFleet(List<ShipPlacement>[] grouped, IReadOnlyList<ShipPlacement> source)
        {
            var result = new List<ShipPlacement>(FleetLayout.ExpectedShipCount);
            
            for (var i = 0; i < BattleshipFleetConfig.StandardFleetOrder.Length; i++)
            {
                var bucket = grouped[(int)BattleshipFleetConfig.StandardFleetOrder[i]];
                
                if (bucket.Count == 0)
                    throw new ArgumentException("Fleet composition does not match expected order.", nameof(source));

                result.Add(bucket[0]);
                bucket.RemoveAt(0);
            }

            return result;
        }

        private static bool TryParseOrientation(string raw, out ShipOrientation orientation)
        {
            orientation = ShipOrientation.Horizontal;

            if (string.Equals(raw, "H", StringComparison.Ordinal))
            {
                orientation = ShipOrientation.Horizontal;
                return true;
            }

            if (string.Equals(raw, "V", StringComparison.Ordinal))
            {
                orientation = ShipOrientation.Vertical;
                return true;
            }

            return false;
        }

        private static bool TryGetStartIndex(in CellId cellId, out int index)
        {
            index = cellId.Major * BattleshipEcsBoard.DefaultBoardSize + cellId.Minor;
            
            return cellId.Major is >= 0 and < BattleshipEcsBoard.DefaultBoardSize
                   && cellId.Minor is >= 0 and < BattleshipEcsBoard.DefaultBoardSize;
        }

        private static bool TryGetStartCell(int index, out CellId cellId)
        {
            cellId = default;
            
            if (index is < 0 or >= BattleshipEcsBoard.DefaultBoardSize * BattleshipEcsBoard.DefaultBoardSize)
                return false;

            var major = index / BattleshipEcsBoard.DefaultBoardSize;
            var minor = index % BattleshipEcsBoard.DefaultBoardSize;
            cellId = new CellId(major, minor);
            return true;
        }
    }
}