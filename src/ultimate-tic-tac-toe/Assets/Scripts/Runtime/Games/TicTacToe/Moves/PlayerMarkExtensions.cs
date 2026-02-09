namespace Runtime.Games.TicTacToe.Moves
{
    public static class PlayerMarkExtensions
    {
        public static string ToUiText(this PlayerMark mark) => mark switch
        {
            PlayerMark.X => "X",
            PlayerMark.O => "O",
            _ => string.Empty,
        };

        /// <summary>
        /// Turn indicator text during InProgress: "Player 1 (X)" / "Player 2 (O)".
        /// TODO: Replace with localized text in Phase 5.
        /// </summary>
        public static string ToTurnIndicatorText(this PlayerMark mark) => mark switch
        {
            PlayerMark.X => "Player 1 (X)",
            PlayerMark.O => "Player 2 (O)",
            _ => string.Empty,
        };

        /// <summary>
        /// Result text for turn indicator: "Player 1 (X) Wins!" / "Draw!".
        /// TODO: Replace with localized text in Phase 5.
        /// </summary>
        public static string ToResultText(this PlayerMark winner, Rules.GameStatus status) => status switch
        {
            Rules.GameStatus.Win when winner == PlayerMark.X => "Player 1 (X) Wins!",
            Rules.GameStatus.Win when winner == PlayerMark.O => "Player 2 (O) Wins!",
            Rules.GameStatus.Draw => "Draw!",
            _ => string.Empty,
        };
    }
}
