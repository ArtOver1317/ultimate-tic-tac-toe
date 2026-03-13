#nullable enable

using Runtime.Gameplay.Shared;

namespace Runtime.Games.Battleship.Core
{
    public static class BattleshipCommandTypes
    {
        public static readonly GameplayCommandType SubmitPlacement = new(nameof(SubmitPlacement));
        public static readonly GameplayCommandType PlacementTimeout = new(nameof(PlacementTimeout));
    }

    public readonly struct SubmitPlacementCommand : IGameplayCommand
    {
        public GameplayCommandType CommandType => BattleshipCommandTypes.SubmitPlacement;
        public int PlayerSlot { get; }
        public FleetLayout Layout { get; }

        public SubmitPlacementCommand(int playerSlot, FleetLayout layout)
        {
            PlayerSlot = playerSlot;
            Layout = layout;
        }
    }

    public readonly struct PlacementTimeoutCommand : IGameplayCommand
    {
        public GameplayCommandType CommandType => BattleshipCommandTypes.PlacementTimeout;
        public int PlayerSlot { get; }
        public int AutoPlaceSeed { get; }

        public PlacementTimeoutCommand(int playerSlot, int autoPlaceSeed)
        {
            PlayerSlot = playerSlot;
            AutoPlaceSeed = autoPlaceSeed;
        }
    }
}