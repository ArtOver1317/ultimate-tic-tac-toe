using Runtime.Games.TicTacToe.Moves;
using Runtime.Games.Battleship;

namespace Runtime.Gameplay.ECS
{
    public enum GameplayCommandType
    {
        MakeMove,
        RestartRound,
        Timeout,
        SubmitPlacement,
        PlacementTimeout,
    }

    public interface IGameplayCommand
    {
        GameplayCommandType CommandType { get; }
    }

    public readonly struct MakeMoveCommand : IGameplayCommand
    {
        public GameplayCommandType CommandType => GameplayCommandType.MakeMove;
        public CellId CellId { get; }
        public MakeMoveCommand(CellId cellId) => CellId = cellId;
    }

    public readonly struct RestartRoundCommand : IGameplayCommand
    {
        public GameplayCommandType CommandType => GameplayCommandType.RestartRound;
        public int StartingPlayerSlot { get; }
        public RestartRoundCommand(int startingPlayerSlot) => StartingPlayerSlot = startingPlayerSlot;
    }

    public readonly struct TimeoutCommand : IGameplayCommand
    {
        public GameplayCommandType CommandType => GameplayCommandType.Timeout;
        public int LoserSlot { get; }
        public TimeoutCommand(int loserSlot) => LoserSlot = loserSlot;
    }

    public readonly struct SubmitPlacementCommand : IGameplayCommand
    {
        public GameplayCommandType CommandType => GameplayCommandType.SubmitPlacement;
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
        public GameplayCommandType CommandType => GameplayCommandType.PlacementTimeout;
        public int PlayerSlot { get; }
        public int AutoPlaceSeed { get; }

        public PlacementTimeoutCommand(int playerSlot, int autoPlaceSeed)
        {
            PlayerSlot = playerSlot;
            AutoPlaceSeed = autoPlaceSeed;
        }
    }
}
