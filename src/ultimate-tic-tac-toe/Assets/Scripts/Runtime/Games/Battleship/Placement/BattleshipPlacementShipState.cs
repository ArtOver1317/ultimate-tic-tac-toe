#nullable enable

using Runtime.Games.Battleship.Core;
using Runtime.Gameplay;

namespace Runtime.Games.Battleship.Placement
{
    public readonly struct BattleshipPlacementShipState
    {
        public int ShipId { get; }

        public ShipSize Size { get; }

        public ShipOrientation Orientation { get; }

        public CellId? StartCell { get; }

        public bool IsPlaced => StartCell.HasValue;

        public BattleshipPlacementShipState(int shipId, ShipSize size, ShipOrientation orientation, CellId? startCell)
        {
            ShipId = shipId;
            Size = size;
            Orientation = orientation;
            StartCell = startCell;
        }
    }
}