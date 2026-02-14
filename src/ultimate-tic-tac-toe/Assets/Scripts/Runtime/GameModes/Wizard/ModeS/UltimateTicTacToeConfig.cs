namespace Runtime.GameModes.Wizard
{
    /// <summary>
    /// Configuration marker for Ultimate Tic-Tac-Toe mode (MVP has no user-editable params).
    /// </summary>
    public sealed class UltimateTicTacToeConfig : IGameConfig
    {
        public static readonly UltimateTicTacToeConfig Instance = new();

        private UltimateTicTacToeConfig() { }
    }
}
