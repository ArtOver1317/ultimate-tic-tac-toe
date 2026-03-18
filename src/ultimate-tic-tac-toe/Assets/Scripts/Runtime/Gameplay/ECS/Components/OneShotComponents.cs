using Runtime.Gameplay.ECS.Pipeline;
using Runtime.Gameplay.ECS.Publishing;
using Runtime.Gameplay.Shared;
using Scellecs.Morpeh;
using EcsGameStatus = Runtime.Gameplay.Shared.EcsGameStatus;

namespace Runtime.Gameplay.ECS.Components
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
    /// Internal pipeline marker: set by the command dispatch stage after one command is consumed during the current tick.
    /// Cleared at the start of the next <see cref="ProcessCommandsSystem"/> update.
    /// </summary>
    public struct CommandDispatchHandledOneShot : IComponent { }

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
        public EcsGameStatus Status;
        public int? WinnerSlot;
        public EcsWinLine? WinLine;
    }

    /// <summary>
    /// One-shot event: placed on match entity by <see cref="Runtime.Games.TicTacToe.ECS.RestartRoundSystem"/>,
    /// consumed by <see cref="EventPublishSystem"/> to publish <see cref="CurrentPlayerChangedEvent"/>.
    /// </summary>
    public struct RoundRestartedOneShot : IComponent { }
}
