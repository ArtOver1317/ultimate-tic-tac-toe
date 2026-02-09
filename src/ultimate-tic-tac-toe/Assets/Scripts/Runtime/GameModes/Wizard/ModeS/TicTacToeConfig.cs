namespace Runtime.GameModes.Wizard
{
    /// <summary>
    /// Configuration for Tic-Tac-Toe game (both classic and ultimate variants).
    /// </summary>
    public sealed class TicTacToeConfig : IGameConfig
    {
        public int BoardSize { get; }
        public bool IsUltimate { get; }

        public TicTacToeConfig(int boardSize, bool isUltimate = false)
        {
            if (boardSize <= 0)
                throw new System.ArgumentOutOfRangeException(nameof(boardSize), boardSize, "BoardSize must be positive.");

            BoardSize = boardSize;
            IsUltimate = isUltimate;
        }
    }
}
