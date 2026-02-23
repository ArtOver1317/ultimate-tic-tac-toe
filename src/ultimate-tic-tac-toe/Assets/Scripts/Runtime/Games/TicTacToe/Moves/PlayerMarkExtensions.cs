using System;
using Runtime.Localization;

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
        public static string ToResultText(this PlayerMark winner, Rules.GameStatus status, ILocalizationService localization = null)
        {
            if (status == Rules.GameStatus.Timeout && localization != null)
            {
                return winner switch
                {
                    PlayerMark.X => ResolveOrFallback(
                        localization,
                        key: "GameOver.TimeoutWin.Player1",
                        fallback: "Player 1 (X) Wins by timeout!"),
                    PlayerMark.O => ResolveOrFallback(
                        localization,
                        key: "GameOver.TimeoutWin.Player2",
                        fallback: "Player 2 (O) Wins by timeout!"),
                    _ => string.Empty,
                };
            }

            return status switch
            {
                Rules.GameStatus.Win when winner == PlayerMark.X => "Player 1 (X) Wins!",
                Rules.GameStatus.Win when winner == PlayerMark.O => "Player 2 (O) Wins!",
                Rules.GameStatus.Timeout when winner == PlayerMark.X => "Player 1 (X) Wins by timeout!",
                Rules.GameStatus.Timeout when winner == PlayerMark.O => "Player 2 (O) Wins by timeout!",
                Rules.GameStatus.Draw => "Draw!",
                _ => string.Empty,
            };
        }

        private static string ResolveOrFallback(ILocalizationService localization, string key, string fallback)
        {
            var resolved = localization.Resolve("GameOver", key);
            return resolved.StartsWith("⟦Missing:", StringComparison.Ordinal) ? fallback : resolved;
        }
    }
}
