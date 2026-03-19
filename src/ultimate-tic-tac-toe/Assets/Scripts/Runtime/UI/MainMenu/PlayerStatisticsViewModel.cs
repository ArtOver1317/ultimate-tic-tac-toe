using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.GameModes.Wizard;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.Modes;
using Runtime.Localization;
using Runtime.PlayerStatistics;
using Runtime.UI.Core;

namespace Runtime.UI.MainMenu
{
    public sealed class PlayerStatisticsGroupPresentation
    {
        public string GameId { get; }
        public string GameTitle { get; }
        public IReadOnlyList<PlayerStatisticsRowPresentation> Rows { get; }

        public PlayerStatisticsGroupPresentation(
            string gameId,
            string gameTitle,
            IReadOnlyList<PlayerStatisticsRowPresentation> rows)
        {
            GameId = gameId;
            GameTitle = gameTitle;
            Rows = rows;
        }
    }

    public sealed class PlayerStatisticsRowPresentation
    {
        public string CompositeLabel { get; }
        public int Wins { get; }
        public int Losses { get; }
        public int Draws { get; }
        public int Total { get; }
        public int WinRatePercent { get; }
        public string BalanceText { get; }

        public PlayerStatisticsRowPresentation(
            string compositeLabel,
            int wins,
            int losses,
            int draws,
            int total,
            int winRatePercent,
            string balanceText)
        {
            CompositeLabel = compositeLabel;
            Wins = wins;
            Losses = losses;
            Draws = draws;
            Total = total;
            WinRatePercent = winRatePercent;
            BalanceText = balanceText;
        }
    }

    public sealed class PlayerStatisticsViewModel : BaseViewModel
    {
        private const string StatisticsTableName = "PlayerStatistics";
        private const string OpponentBotKey = "GameWizard.MatchSetup.Opponent.Bot";
        private const string OpponentHotSeatKey = "GameWizard.MatchSetup.HumanSettings.Local";
        private const string OpponentOnlineKey = "PlayerStatistics.Opponent.Online";

        private readonly IPlayerStatisticsService _statisticsService;
        private readonly IGameCatalog _gameCatalog;
        private readonly IBotDifficultyCatalog _botDifficultyCatalog;
        private readonly ILocalizationService _localization;
        private readonly Dictionary<string, string> _botDifficultyLocalizationKeyById;
        private readonly Dictionary<string, int> _botDifficultySortOrderById;
        private readonly Subject<Unit> _backRequested = new();
        private readonly ReactiveProperty<IReadOnlyList<PlayerStatisticsGroupPresentation>> _groups =
            new(Array.Empty<PlayerStatisticsGroupPresentation>());
        private readonly ReactiveProperty<bool> _isEmpty = new(true);

        public Observable<string> TitleText { get; }
        public Observable<string> BackButtonText { get; }
        public Observable<string> EmptyStateText { get; }
        public ReadOnlyReactiveProperty<IReadOnlyList<PlayerStatisticsGroupPresentation>> Groups => _groups;
        public ReadOnlyReactiveProperty<bool> IsEmpty => _isEmpty;
        public Observable<Unit> BackRequested => _backRequested;

        public PlayerStatisticsViewModel(
            IPlayerStatisticsService statisticsService,
            IGameCatalog gameCatalog,
            IBotDifficultyCatalog botDifficultyCatalog,
            ILocalizationService localization)
        {
            _statisticsService = statisticsService ?? throw new ArgumentNullException(nameof(statisticsService));
            _gameCatalog = gameCatalog ?? throw new ArgumentNullException(nameof(gameCatalog));
            _botDifficultyCatalog = botDifficultyCatalog ?? throw new ArgumentNullException(nameof(botDifficultyCatalog));
            _localization = localization ?? throw new ArgumentNullException(nameof(localization));

            (_botDifficultyLocalizationKeyById, _botDifficultySortOrderById) =
                BuildBotDifficultyMaps(_botDifficultyCatalog.Difficulties);

            var table = new TextTableId(StatisticsTableName);
            TitleText = _localization.Observe(table, new TextKey("PlayerStatistics.Title"));
            BackButtonText = _localization.Observe(table, new TextKey("PlayerStatistics.Back"));
            EmptyStateText = _localization.Observe(table, new TextKey("PlayerStatistics.Empty"));
        }

        public UniTask PreloadOnOpenAsync(CancellationToken cancellationToken) =>
            _localization.PreloadAsync(
                _localization.CurrentLocale.CurrentValue,
                new[]
                {
                    new TextTableId(StatisticsTableName),
                    new TextTableId("Game"),
                    new TextTableId("GameWizard"),
                },
                cancellationToken);

        public override void Initialize()
        {
            base.Initialize();
            Rebuild();
        }

        public void RequestBack()
        {
            _backRequested.OnNext(Unit.Default);
            RequestClose();
        }

        internal void Rebuild()
        {
            var snapshot = _statisticsService.GetEntriesSnapshot();

            if (snapshot == null || snapshot.Count == 0)
            {
                SetEmpty();
                return;
            }

            var (strategyById, gameOrderById, supportedBotDifficultyIds) = BuildCatalogLookups();
            var entriesByGameId = FilterAndGroupEntries(snapshot, strategyById, supportedBotDifficultyIds);

            if (entriesByGameId.Count == 0)
            {
                SetEmpty();
                return;
            }

            _groups.Value = BuildGroups(entriesByGameId, strategyById, gameOrderById);
            _isEmpty.Value = false;
        }

        protected override void OnDispose()
        {
            _backRequested.OnCompleted();
            _backRequested.Dispose();
            _groups.Dispose();
            _isEmpty.Dispose();
        }

        private void SetEmpty()
        {
            _groups.Value = Array.Empty<PlayerStatisticsGroupPresentation>();
            _isEmpty.Value = true;
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

        private static bool IsSupportedEntry(
            StatisticsEntry entry,
            Dictionary<string, IGameStrategy> strategyById,
            HashSet<string> supportedBotDifficultyIds)
        {
            if (!strategyById.ContainsKey(entry.Key.GameId))
                return false;

            if (entry.Key.OpponentType != StatisticsOpponentType.Bot)
                return true;

            if (string.IsNullOrWhiteSpace(entry.Key.BotDifficultyId))
                return false;

            return supportedBotDifficultyIds.Contains(entry.Key.BotDifficultyId);
        }

        private List<PlayerStatisticsGroupPresentation> BuildGroups(
            Dictionary<string, List<StatisticsEntry>> entriesByGameId,
            Dictionary<string, IGameStrategy> strategyById,
            Dictionary<string, int> gameOrderById)
        {
            var orderedGameIds = entriesByGameId.Keys
                .OrderBy(id => gameOrderById.TryGetValue(id, out var order) ? order : int.MaxValue)
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
                : (int)Math.Round((double)wins * 100d / total, MidpointRounding.AwayFromZero);
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

        private string ResolveOpponentLabel(MatchKey key) => key.OpponentType switch
        {
            StatisticsOpponentType.HotSeat => ResolveLocalizedKeyOrFallback(OpponentHotSeatKey, "HotSeat"),
            StatisticsOpponentType.Bot => ResolveBotLabel(key.BotDifficultyId),
            StatisticsOpponentType.Online => ResolveLocalizedKeyOrFallback(OpponentOnlineKey, "Online"),
            _ => "Unknown",
        };

        private string BuildConfigurationLabel(MatchKey key, string gameLabel)
        {
            var opponentLabel = ResolveOpponentLabel(key);
            return $"{gameLabel} · {opponentLabel}";
        }

        private string ResolveBotLabel(string botDifficultyId)
        {
            var botLabel = ResolveLocalizedKeyOrFallback(OpponentBotKey, "Bot");

            if (string.IsNullOrWhiteSpace(botDifficultyId))
                return botLabel;

            var difficultyLabel = ResolveBotDifficultyLabel(botDifficultyId);
            return $"{botLabel} {difficultyLabel}";
        }

        private string ResolveBotDifficultyLabel(string botDifficultyId)
        {
            if (string.IsNullOrWhiteSpace(botDifficultyId))
                return string.Empty;

            if (!_botDifficultyLocalizationKeyById.TryGetValue(botDifficultyId, out var key))
                return botDifficultyId;

            return ResolveLocalizedKeyOrFallback(key, botDifficultyId);
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

            if (string.IsNullOrWhiteSpace(resolved))
                return fallback;

            if (string.Equals(resolved, localizationKey, StringComparison.Ordinal))
                return fallback;

            if (resolved.StartsWith("[[", StringComparison.Ordinal) && resolved.EndsWith("]]", StringComparison.Ordinal))
                return fallback;

            return resolved;
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

        private int ResolveBotDifficultySortOrder(string botDifficultyId)
        {
            if (string.IsNullOrWhiteSpace(botDifficultyId))
                return int.MaxValue;

            if (_botDifficultySortOrderById.TryGetValue(botDifficultyId, out var sortOrder))
                return sortOrder;

            return int.MaxValue;
        }
    }
}
