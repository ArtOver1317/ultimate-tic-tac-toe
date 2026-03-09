using System.Collections.Generic;

namespace Runtime.GameModes.Wizard
{
    /// <summary>
    /// Configuration marker for Ultimate Tic-Tac-Toe mode (MVP has no user-editable params).
    /// </summary>
    public sealed class UltimateTicTacToeConfig : IGameConfig
    {
        private static readonly IReadOnlyList<KeyValuePair<string, string>> MatchmakingParams =
            System.Array.Empty<KeyValuePair<string, string>>();

        public static readonly UltimateTicTacToeConfig Instance = new();

        private UltimateTicTacToeConfig() { }

        public IReadOnlyList<KeyValuePair<string, string>> GetMatchmakingParams() => MatchmakingParams;
    }
}
