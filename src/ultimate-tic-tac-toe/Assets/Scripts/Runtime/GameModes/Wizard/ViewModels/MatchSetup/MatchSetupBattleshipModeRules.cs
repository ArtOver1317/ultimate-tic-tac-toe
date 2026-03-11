#nullable enable

using System;
using System.Collections.Generic;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.Modes;
using Runtime.GameModes.Wizard.Session;

namespace Runtime.GameModes.Wizard.ViewModels.MatchSetup
{
    internal static class MatchSetupBattleshipModeRules
    {
        public static bool IsBattleshipGame(string? gameId) =>
            string.Equals(gameId, BattleshipStrategy.DefaultGameId, StringComparison.Ordinal);

        public static IReadOnlyList<BotDifficulty> SelectAvailableDifficulties(
            string? gameId,
            IReadOnlyList<BotDifficulty> source)
        {
            if (!IsBattleshipGame(gameId))
                return source;

            var result = new List<BotDifficulty>(capacity: 1);

            foreach (var difficulty in source)
            {
                if (difficulty == null)
                    continue;

                if (!string.Equals(difficulty.Id, BattleshipStrategy.DefaultBotDifficultyId, StringComparison.Ordinal))
                    continue;

                result.Add(difficulty);
                break;
            }

            return result.Count == 0
                ? Array.Empty<BotDifficulty>()
                : Array.AsReadOnly(result.ToArray());
        }

        public static bool RequiresDirectInvite(GameSessionSnapshot snapshot) =>
            IsBattleshipGame(snapshot.SelectedGameId)
            && snapshot is { OpponentType: OpponentType.Human, HumanOpponentKind: HumanOpponentKind.Local };

        public static bool ShouldApplyDefaultDifficulty(
            GameSessionSnapshot snapshot,
            Func<string, bool> isDifficultyAvailable) =>
            IsBattleshipGame(snapshot.SelectedGameId)
            && snapshot.OpponentType == OpponentType.Bot
            && string.IsNullOrWhiteSpace(snapshot.BotDifficultyId)
            && isDifficultyAvailable(BattleshipStrategy.DefaultBotDifficultyId);

        public static bool ShouldHideBotDifficulty(string? gameId, int availableDifficultyCount) =>
            IsBattleshipGame(gameId)
            && availableDifficultyCount <= 1;
    }
}