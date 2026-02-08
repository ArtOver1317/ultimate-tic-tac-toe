namespace Runtime.GameModes.Wizard
{
    public sealed class ClassicModeConfig : IGameModeConfig
    {
        public int BoardSize { get; }

        public ClassicModeConfig(int boardSize)
        {
            if (boardSize <= 0)
                throw new System.ArgumentOutOfRangeException(nameof(boardSize), boardSize, "BoardSize must be positive.");

            BoardSize = boardSize;
        }
    }
}
