using Runtime.Gameplay;
using Runtime.Localization;

namespace Runtime.Games.TicTacToe.Moves
{
    public static class PlayerMarkExtensions
    {
        private const string _gameTable = "Game";
        private const string _gameOverTable = "GameOver";

        public static string ToUiText(this PlayerMark mark) => mark switch
        {
            PlayerMark.X => "X",
            PlayerMark.O => "O",
            _ => string.Empty,
        };

        public static string ToTurnIndicatorText(this PlayerMark mark, ILocalizationService localization = null) => mark switch
        {
            PlayerMark.X => ResolveOrFallback(localization, _gameTable, "Game.PlayerTurn.Player1", "Player 1 (X)"),
            PlayerMark.O => ResolveOrFallback(localization, _gameTable, "Game.PlayerTurn.Player2", "Player 2 (O)"),
            _ => string.Empty,
        };

        public static string ToResultText(this PlayerMark winner, GameStatus status, ILocalizationService localization = null) => status switch
        {
            GameStatus.Win when winner == PlayerMark.X => ResolveOrFallback(localization, _gameOverTable, "GameOver.Win.Player1", "Player 1 (X) Wins!"),
            GameStatus.Win when winner == PlayerMark.O => ResolveOrFallback(localization, _gameOverTable, "GameOver.Win.Player2", "Player 2 (O) Wins!"),
            GameStatus.Timeout when winner == PlayerMark.X => ResolveOrFallback(localization, _gameOverTable, "GameOver.TimeoutWin.Player1", "Player 1 (X) Wins by timeout!"),
            GameStatus.Timeout when winner == PlayerMark.O => ResolveOrFallback(localization, _gameOverTable, "GameOver.TimeoutWin.Player2", "Player 2 (O) Wins by timeout!"),
            GameStatus.Draw => ResolveOrFallback(localization, _gameOverTable, "GameOver.Draw", "Draw!"),
            _ => string.Empty,
        };

        private static string ResolveOrFallback(ILocalizationService localization, string table, string key, string fallback)
        {
            if (localization == null)
                return fallback;

            return localization.TryResolve(table, key, out var resolved) ? resolved : fallback;
        }
    }
}