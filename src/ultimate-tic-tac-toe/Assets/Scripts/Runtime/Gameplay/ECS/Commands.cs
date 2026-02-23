using Runtime.Games.TicTacToe.Moves;

namespace Runtime.Gameplay.ECS
{
    public enum GameplayCommandType
    {
        MakeMove,
        RestartRound,
        Timeout,
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
}
