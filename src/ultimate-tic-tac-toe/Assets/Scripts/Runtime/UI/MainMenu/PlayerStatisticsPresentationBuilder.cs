using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.Modes;
using Runtime.Localization.Contracts;
using Runtime.Localization.Types;
using Runtime.PlayerStatistics;

namespace Runtime.UI.MainMenu
{
    internal sealed class PlayerStatisticsPresentationBuilder
    {
        private const string _opponentBotKey = "GameWizard.MatchSetup.Opponent.Bot";
        private const string _opponentHotSeatKey = "GameWizard.MatchSetup.HumanSettings.Local";
        private const string _opponentOnlineKey = "PlayerStatistics.Opponent.Online";

        private readonly IGameCatalog _gameCatalog;
        private readonly ILocalizationService _localization;
        private readonly Dictionary<string, string> _botDifficultyLocalizationKeyById;
        private readonly Dictionary<string, int> _botDifficultySortOrderById;

        public PlayerStatisticsPresentationBuilder(
            IGameCatalog gameCatalog,
            IBotDifficultyCatalog botDifficultyCatalog,
            ILocalizationService localization)
        {
            _gameCatalog = gameCatalog ?? throw new ArgumentNullException(nameof(gameCatalog));
            var resolvedBotDifficultyCatalog = botDifficultyCatalog ?? throw new ArgumentNullException(nameof(botDifficultyCatalog));
            _localization = localization ?? throw new ArgumentNullException(nameof(localization));

            (_botDifficultyLocalizationKeyById, _botDifficultySortOrderById) =
                BuildBotDifficultyMaps(resolvedBotDifficultyCatalog.Difficulties);
        }

        public IReadOnlyList<PlayerStatisticsGroupPresentation> BuildGroups(IReadOnlyList<StatisticsEntry> snapshot)
        {
            var (strategyById, gameOrderById, supportedBotDifficultyIds) = BuildCatalogLookups();
            var entriesByGameId = FilterAndGroupEntries(snapshot, strategyById, supportedBotDifficultyIds);

            if (entriesByGameId.Count == 0)
                return Array.Empty<PlayerStatisticsGroupPresentation>();

            return BuildGroups(entriesByGameId, strategyById, gameOrderById);
        }

        private (
            Dictionary<string, IGameStrategy> strategyById,
            Dictionary<string, int> gameOrderById,
            HashSet<string> supportedBotDifficultyIds)
            BuildCatalogLookups()
        {
            var strategyById = new Dictionary<string, IGameStrategy>(StringComparer.Ordinal);
            var gameOrderById = new Dictionary<string, int>(StringComparer.Ordinal);
            var supportedBotDifficultyIds = new HashSet<string>(StringComparer.Ordinal);

            for (var i = 0; i < _gameCatalog.Strategies.Count; i++)
            {
                var strategy = _gameCatalog.Strategies[i];
                strategyById[strategy.GameId] = strategy;
                gameOrderById[strategy.GameId] = i;

                foreach (var difficultyId in strategy.GetSupportedBotDifficultyIds())
                {
                    if (!string.IsNullOrWhiteSpace(difficultyId))
                        supportedBotDifficultyIds.Add(difficultyId.Trim());
                }
            }

            return (strategyById, gameOrderById, supportedBotDifficultyIds);
        }

        private List<PlayerStatisticsGroupPresentation> BuildGroups(
            Dictionary<string, List<StatisticsEntry>> entriesByGameId,
            Dictionary<string, IGameStrategy> strategyById,
            Dictionary<string, int> gameOrderById)
        {
            var orderedGameIds = entriesByGameId.Keys
                .OrderBy(id => gameOrderById.GetValueOrDefault(id, int.MaxValue))
                .ThenBy(id => id, StringComparer.Ordinal)
                .ToArray();

            var groups = new List<PlayerStatisticsGroupPresentation>(orderedGameIds.Length);

            for (var i = 0; i < orderedGameIds.Length; i++)
            {
                var gameId = orderedGameIds[i];
                var strategy = strategyById[gameId];
                var localizedTitle = ResolveLocalizedKeyOrFallback(strategy.Metadata.DisplayNameKey, gameId);
                
                var rows = OrderEntriesForDisplay(entriesByGameId[gameId])
                    .Select(entry => BuildRowPresentation(entry, localizedTitle))
                    .ToArray();

                groups.Add(new PlayerStatisticsGroupPresentation(gameId, localizedTitle, rows));
            }

            return groups;
        }

        private static Dictionary<string, List<StatisticsEntry>> FilterAndGroupEntries(
            IReadOnlyList<StatisticsEntry> snapshot,
            Dictionary<string, IGameStrategy> strategyById,
            HashSet<string> supportedBotDifficultyIds)
        {
            var entriesByGameId = new Dictionary<string, List<StatisticsEntry>>(StringComparer.Ordinal);

            for (var i = 0; i < snapshot.Count; i++)
            {
                var entry = snapshot[i];
                
                if (!IsSupportedEntry(entry, strategyById, supportedBotDifficultyIds))
                    continue;

                if (!entriesByGameId.TryGetValue(entry.Key.GameId, out var entries))
                {
                    entries = new List<StatisticsEntry>();
                    entriesByGameId.Add(entry.Key.GameId, entries);
                }

                entries.Add(entry);
            }

            return entriesByGameId;
        }

        private IReadOnlyList<StatisticsEntry> OrderEntriesForDisplay(IReadOnlyList<StatisticsEntry> entries) =>
            entries
                .Select((entry, index) => new { Entry = entry, OriginalIndex = index })
                .OrderBy(item => item.Entry.Key.OpponentType == StatisticsOpponentType.Bot ? 0 : 1)
                .ThenBy(item => item.Entry.Key.OpponentType == StatisticsOpponentType.Bot
                    ? ResolveBotDifficultySortOrder(item.Entry.Key.BotDifficultyId)
                    : int.MaxValue)
                .ThenBy(item => item.OriginalIndex)
                .Select(item => item.Entry)
                .ToArray();

        private PlayerStatisticsRowPresentation BuildRowPresentation(StatisticsEntry entry, string gameLabel)
        {
            var wins = entry.Record.Wins;
            var losses = entry.Record.Losses;
            var draws = entry.Record.Draws;
            var total = wins + losses + draws;
           
            var winRate = total == 0
                ? 0
                : (int)Math.Round(wins * 100d / total, MidpointRounding.AwayFromZero);
            
            var balance = wins - losses;
            
            var balanceText = balance > 0
                ? $"+{balance.ToString(CultureInfo.InvariantCulture)}"
                : balance.ToString(CultureInfo.InvariantCulture);

            var configurationLabel = BuildConfigurationLabel(entry.Key, gameLabel);
           
            var winRateSegment = total > 0
                ? $" · Win% {winRate}%"
                : string.Empty;
           
            var compositeLabel =
                $"{configurationLabel}: W {wins} / L {losses} / D {draws}{winRateSegment} · Total {total} · Balance {balanceText}";

            return new PlayerStatisticsRowPresentation(
                compositeLabel,
                wins,
                losses,
                draws,
                total,
                winRate,
                balanceText);
        }

        private string BuildConfigurationLabel(MatchKey key, string gameLabel)
        {
            var opponentLabel = ResolveOpponentLabel(key);
            return $"{gameLabel} · {opponentLabel}";
        }

        private string ResolveOpponentLabel(MatchKey key) => key.OpponentType switch
        {
            StatisticsOpponentType.HotSeat => ResolveLocalizedKeyOrFallback(_opponentHotSeatKey, "HotSeat"),
            StatisticsOpponentType.Bot => ResolveBotLabel(key.BotDifficultyId),
            StatisticsOpponentType.Online => ResolveLocalizedKeyOrFallback(_opponentOnlineKey, "Online"),
            _ => "Unknown",
        };

        private string ResolveBotLabel(string botDifficultyId)
        {
            var botLabel = ResolveLocalizedKeyOrFallback(_opponentBotKey, "Bot");

            if (string.IsNullOrWhiteSpace(botDifficultyId))
                return botLabel;

            var difficultyLabel = ResolveBotDifficultyLabel(botDifficultyId);
            return $"{botLabel} {difficultyLabel}";
        }

        private string ResolveBotDifficultyLabel(string botDifficultyId)
        {
            if (string.IsNullOrWhiteSpace(botDifficultyId))
                return string.Empty;

            return !_botDifficultyLocalizationKeyById.TryGetValue(botDifficultyId, out var key) 
                ? botDifficultyId 
                : ResolveLocalizedKeyOrFallback(key, botDifficultyId);
        }

        private string ResolveLocalizedKeyOrFallback(string localizationKey, string fallback)
        {
            if (string.IsNullOrWhiteSpace(localizationKey))
                return fallback;

            var dotIndex = localizationKey.IndexOf('.', StringComparison.Ordinal);
           
            if (dotIndex <= 0)
                return fallback;

            var tableName = localizationKey[..dotIndex];
            var resolved = _localization.Resolve(new TextTableId(tableName), new TextKey(localizationKey));

            if (string.IsNullOrWhiteSpace(resolved) 
                || string.Equals(resolved, localizationKey, StringComparison.Ordinal) 
                || resolved.StartsWith("[[", StringComparison.Ordinal) && resolved.EndsWith("]]", StringComparison.Ordinal))
                return fallback;

            return resolved;
        }

        private int ResolveBotDifficultySortOrder(string botDifficultyId) =>
            string.IsNullOrWhiteSpace(botDifficultyId) 
                ? int.MaxValue 
                : _botDifficultySortOrderById.GetValueOrDefault(botDifficultyId, int.MaxValue);

        private static bool IsSupportedEntry(
            StatisticsEntry entry,
            Dictionary<string, IGameStrategy> strategyById,
            HashSet<string> supportedBotDifficultyIds)
        {
            if (!strategyById.ContainsKey(entry.Key.GameId))
                return false;

            if (entry.Key.OpponentType != StatisticsOpponentType.Bot)
                return true;

            return !string.IsNullOrWhiteSpace(entry.Key.BotDifficultyId) 
                   && supportedBotDifficultyIds.Contains(entry.Key.BotDifficultyId);
        }

        private static (Dictionary<string, string> localizationKeyById, Dictionary<string, int> sortOrderById)
            BuildBotDifficultyMaps(IReadOnlyList<BotDifficulty> difficulties)
        {
            var localizationKeyById = new Dictionary<string, string>(StringComparer.Ordinal);
            var sortOrderById = new Dictionary<string, int>(StringComparer.Ordinal);

            if (difficulties == null)
                return (localizationKeyById, sortOrderById);

            for (var i = 0; i < difficulties.Count; i++)
            {
                var difficulty = difficulties[i];

                if (string.IsNullOrWhiteSpace(difficulty.Id))
                    continue;

                if (!sortOrderById.ContainsKey(difficulty.Id))
                    sortOrderById.Add(difficulty.Id, difficulty.SortOrder);

                if (!string.IsNullOrWhiteSpace(difficulty.NameKey) && !localizationKeyById.ContainsKey(difficulty.Id))
                    localizationKeyById.Add(difficulty.Id, difficulty.NameKey);
            }

            return (localizationKeyById, sortOrderById);
        }
    }
}