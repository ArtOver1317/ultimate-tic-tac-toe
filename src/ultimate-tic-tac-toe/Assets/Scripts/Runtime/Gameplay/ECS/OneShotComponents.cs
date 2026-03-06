using Runtime.Games.TicTacToe.Moves;
using Scellecs.Morpeh;

namespace Runtime.Gameplay.ECS
{
    /// <summary>
    /// One-shot request: created by <see cref="ProcessCommandsSystem"/>, consumed by game-specific validation system.
    /// </summary>
    public struct MakeMoveRequest : IComponent
    {
        public CellId CellId;
    }

    /// <summary>
    /// One-shot request: created by <see cref="ProcessCommandsSystem"/>, consumed by game-specific restart system.
    /// </summary>
    public struct RestartRoundRequest : IComponent
    {
        public int StartingPlayerSlot;
    }

    /// <summary>
    /// One-shot request: created by <see cref="ProcessCommandsSystem"/>, consumed by <see cref="TimeoutTerminalSystem"/>.
    /// </summary>
    public struct TimeoutRequest : IComponent
    {
        public int LoserSlot;
    }

    /// <summary>
    /// One-shot request: created by <see cref="ProcessCommandsSystem"/>, consumed by battleship placement system.
    /// </summary>
    public struct SubmitPlacementRequest : IComponent
    {
        public int PlayerSlot;
        public Runtime.Games.Battleship.FleetLayout Layout;
    }

    /// <summary>
    /// One-shot request: created by <see cref="ProcessCommandsSystem"/>, consumed by battleship placement system.
    /// </summary>
    public struct PlacementTimeoutRequest : IComponent
    {
        public int PlayerSlot;
        public int AutoPlaceSeed;
    }

    /// <summary>
    /// One-shot event: placed on match entity by apply systems, consumed by <see cref="EventPublishSystem"/>.
    /// </summary>
    public struct MoveAppliedOneShot : IComponent
    {
        public CellId CellId;
        public int PlayerSlot;
    }

    /// <summary>
    /// One-shot event: placed on match entity by validation systems, consumed by <see cref="EventPublishSystem"/>.
    /// </summary>
    public struct MoveRejectedOneShot : IComponent
    {
        public GameplayCommandType CommandType;
        public CommandRejection Rejection;
    }

    /// <summary>
    /// One-shot event: placed on match entity by rules evaluation, consumed by <see cref="EventPublishSystem"/>.
    /// </summary>
    public struct RoundFinishedOneShot : IComponent
    {
        public GameStatus Status;
        public int? WinnerSlot;
        public EcsWinLine? WinLine;
    }

    /// <summary>
    /// One-shot event: placed on match entity by <see cref="Runtime.Games.TicTacToe.ECS.RestartRoundSystem"/>,
    /// consumed by <see cref="EventPublishSystem"/> to publish <see cref="CurrentPlayerChangedEvent"/>.
    /// </summary>
    public struct RoundRestartedOneShot : IComponent { }
}
