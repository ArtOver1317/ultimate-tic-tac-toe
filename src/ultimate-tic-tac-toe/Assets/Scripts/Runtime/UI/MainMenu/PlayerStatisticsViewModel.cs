using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.Modes;
using Runtime.Localization.Contracts;
using Runtime.Localization.Types;
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
        private const string _statisticsTableName = "PlayerStatistics";

        private readonly IPlayerStatisticsService _statisticsService;
        private readonly ILocalizationService _localization;
        private readonly PlayerStatisticsPresentationBuilder _presentationBuilder;
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
            _localization = localization ?? throw new ArgumentNullException(nameof(localization));
           
            _presentationBuilder = new PlayerStatisticsPresentationBuilder(
                gameCatalog ?? throw new ArgumentNullException(nameof(gameCatalog)),
                botDifficultyCatalog ?? throw new ArgumentNullException(nameof(botDifficultyCatalog)),
                _localization);

            var table = new TextTableId(_statisticsTableName);
            TitleText = _localization.Observe(table, new TextKey("PlayerStatistics.Title"));
            BackButtonText = _localization.Observe(table, new TextKey("PlayerStatistics.Back"));
            EmptyStateText = _localization.Observe(table, new TextKey("PlayerStatistics.Empty"));
        }

        public UniTask PreloadOnOpenAsync(CancellationToken cancellationToken) =>
            _localization.PreloadAsync(
                _localization.CurrentLocale.CurrentValue,
                new[]
                {
                    new TextTableId(_statisticsTableName),
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

        private void Rebuild()
        {
            var snapshot = _statisticsService.GetEntriesSnapshot();

            if (snapshot == null || snapshot.Count == 0)
            {
                SetEmpty();
                return;
            }

            var groups = _presentationBuilder.BuildGroups(snapshot);
            
            if (groups.Count == 0)
            {
                SetEmpty();
                return;
            }

            _groups.Value = groups;
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
    }
}