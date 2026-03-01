using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using R3;
using Runtime.GameModes.Wizard;
using Runtime.Localization;
using Runtime.PlayerStatistics;
using Runtime.UI.MainMenu;

namespace Tests.EditMode.PlayerStatistics
{
    [TestFixture]
    [Category("Unit")]
    public sealed class PlayerStatisticsViewModelTests
    {
        [Test]
        public void WhenWinsLossesDrawsAreOne_ThenComputesDerivedMetrics()
        {
            var localization = CreateLocalizationStub();
            var statisticsService = Substitute.For<IPlayerStatisticsService>();
            statisticsService.GetEntriesSnapshot().Returns(new[]
            {
                new StatisticsEntry(
                    new MatchKey("tic-tac-toe", StatisticsOpponentType.HotSeat, null),
                    new StatisticsRecord(1, 1, 1)),
            });

            var strategy = CreateStrategy("tic-tac-toe", 10, "Game.TicTacToe", Array.Empty<string>());
            var catalog = CreateCatalog(strategy);
            var botCatalog = CreateBotCatalog();

            var sut = new PlayerStatisticsViewModel(statisticsService, catalog, botCatalog, localization);
            sut.Initialize();

            var groups = sut.Groups.CurrentValue;
            groups.Should().HaveCount(1);
            groups[0].Rows.Should().HaveCount(1);

            var row = groups[0].Rows[0];
            row.WinRatePercent.Should().Be(33);
            row.Total.Should().Be(3);
            row.BalanceText.Should().Be("0");
        }

        [Test]
        public void WhenServiceReturnsNoEntries_ThenShowsEmptyState()
        {
            var localization = CreateLocalizationStub();
            var statisticsService = Substitute.For<IPlayerStatisticsService>();
            statisticsService.GetEntriesSnapshot().Returns(Array.Empty<StatisticsEntry>());

            var strategy = CreateStrategy("tic-tac-toe", 10, "Game.TicTacToe", Array.Empty<string>());
            var catalog = CreateCatalog(strategy);
            var botCatalog = CreateBotCatalog();

            var sut = new PlayerStatisticsViewModel(statisticsService, catalog, botCatalog, localization);
            sut.Initialize();

            statisticsService.Received(1).GetEntriesSnapshot();
            sut.IsEmpty.CurrentValue.Should().BeTrue();
            sut.Groups.CurrentValue.Should().BeEmpty();
        }

        [Test]
        public void WhenEntryHasUnknownGameId_ThenFiltersEntryOut()
        {
            var localization = CreateLocalizationStub();
            var statisticsService = Substitute.For<IPlayerStatisticsService>();
            statisticsService.GetEntriesSnapshot().Returns(new[]
            {
                new StatisticsEntry(
                    new MatchKey("unknown-game", StatisticsOpponentType.HotSeat, null),
                    new StatisticsRecord(2, 1, 0)),
            });

            var strategy = CreateStrategy("tic-tac-toe", 10, "Game.TicTacToe", Array.Empty<string>());
            var catalog = CreateCatalog(strategy);
            var botCatalog = CreateBotCatalog();

            var sut = new PlayerStatisticsViewModel(statisticsService, catalog, botCatalog, localization);
            sut.Initialize();

            sut.IsEmpty.CurrentValue.Should().BeTrue();
            sut.Groups.CurrentValue.Should().BeEmpty();
        }

        [Test]
        public void WhenGameHasMixedOpponentsAndBotDifficulties_ThenRowsGroupBotsAndSortByDifficulty()
        {
            var localization = CreateLocalizationStub();
            var statisticsService = Substitute.For<IPlayerStatisticsService>();
            statisticsService.GetEntriesSnapshot().Returns(new[]
            {
                new StatisticsEntry(
                    new MatchKey("tic-tac-toe", StatisticsOpponentType.Bot, "Normal"),
                    new StatisticsRecord(1, 0, 0)),
                new StatisticsEntry(
                    new MatchKey("tic-tac-toe", StatisticsOpponentType.HotSeat, null),
                    new StatisticsRecord(1, 0, 0)),
                new StatisticsEntry(
                    new MatchKey("tic-tac-toe", StatisticsOpponentType.Bot, "Easy"),
                    new StatisticsRecord(1, 0, 0)),
            });

            var strategy = CreateStrategy("tic-tac-toe", 10, "Game.TicTacToe", new[] { "Easy", "Normal", "Hard" });
            var catalog = CreateCatalog(strategy);
            var botCatalog = CreateBotCatalog();

            var sut = new PlayerStatisticsViewModel(statisticsService, catalog, botCatalog, localization);
            sut.Initialize();

            var rows = sut.Groups.CurrentValue[0].Rows;
            rows.Should().HaveCount(3);
            rows[0].CompositeLabel.Should().Contain("Bot").And.Contain("Easy");
            rows[1].CompositeLabel.Should().Contain("Bot").And.Contain("Normal");
            rows[2].CompositeLabel.Should().Contain("Local");
        }

        [Test]
        public void WhenBuildingRowLabel_ThenUsesComposedConfigurationFormat()
        {
            var localization = CreateLocalizationStub();
            var statisticsService = Substitute.For<IPlayerStatisticsService>();
            statisticsService.GetEntriesSnapshot().Returns(new[]
            {
                new StatisticsEntry(
                    new MatchKey("tic-tac-toe", StatisticsOpponentType.Online, null),
                    new StatisticsRecord(2, 1, 0)),
            });

            var strategy = CreateStrategy("tic-tac-toe", 10, "Game.TicTacToe", new[] { "Easy", "Normal", "Hard" });
            var catalog = CreateCatalog(strategy);
            var botCatalog = CreateBotCatalog();

            var sut = new PlayerStatisticsViewModel(statisticsService, catalog, botCatalog, localization);
            sut.Initialize();

            var row = sut.Groups.CurrentValue[0].Rows[0];
            row.CompositeLabel.Should().Contain("Tic-Tac-Toe").And.Contain("Online");
        }

        [Test]
        public void WhenTotalMatchesIsZero_ThenCompositeLabelDoesNotContainWinPercent()
        {
            var localization = CreateLocalizationStub();
            var statisticsService = Substitute.For<IPlayerStatisticsService>();
            statisticsService.GetEntriesSnapshot().Returns(new[]
            {
                new StatisticsEntry(
                    new MatchKey("tic-tac-toe", StatisticsOpponentType.HotSeat, null),
                    new StatisticsRecord(0, 0, 0)),
            });

            var strategy = CreateStrategy("tic-tac-toe", 10, "Game.TicTacToe", Array.Empty<string>());
            var catalog = CreateCatalog(strategy);
            var botCatalog = CreateBotCatalog();

            var sut = new PlayerStatisticsViewModel(statisticsService, catalog, botCatalog, localization);
            sut.Initialize();

            var row = sut.Groups.CurrentValue[0].Rows[0];
            row.CompositeLabel.Should().NotContain("Win%");
            row.Total.Should().Be(0);
        }

        [Test]
        public void WhenBotEntryHasUnknownDifficultyId_ThenRowIsFilteredOut()
        {
            var localization = CreateLocalizationStub();
            var statisticsService = Substitute.For<IPlayerStatisticsService>();
            statisticsService.GetEntriesSnapshot().Returns(new[]
            {
                new StatisticsEntry(
                    new MatchKey("tic-tac-toe", StatisticsOpponentType.Bot, "Nightmare"),
                    new StatisticsRecord(2, 1, 0)),
            });

            var strategy = CreateStrategy("tic-tac-toe", 10, "Game.TicTacToe", new[] { "Easy", "Normal", "Hard" });
            var catalog = CreateCatalog(strategy);
            var botCatalog = CreateBotCatalog();

            var sut = new PlayerStatisticsViewModel(statisticsService, catalog, botCatalog, localization);
            sut.Initialize();

            sut.IsEmpty.CurrentValue.Should().BeTrue();
            sut.Groups.CurrentValue.Should().BeEmpty();
        }

        [Test]
        public void WhenBotEntryHasNullDifficultyId_ThenRowIsFilteredOut()
        {
            var localization = CreateLocalizationStub();
            var statisticsService = Substitute.For<IPlayerStatisticsService>();
            statisticsService.GetEntriesSnapshot().Returns(new[]
            {
                new StatisticsEntry(
                    new MatchKey("tic-tac-toe", StatisticsOpponentType.Bot, null),
                    new StatisticsRecord(3, 3, 3)),
            });

            var strategy = CreateStrategy("tic-tac-toe", 10, "Game.TicTacToe", new[] { "Easy", "Normal", "Hard" });
            var catalog = CreateCatalog(strategy);
            var botCatalog = CreateBotCatalog();

            var sut = new PlayerStatisticsViewModel(statisticsService, catalog, botCatalog, localization);
            sut.Initialize();

            sut.IsEmpty.CurrentValue.Should().BeTrue();
            sut.Groups.CurrentValue.Should().BeEmpty();
        }

        [Test]
        public void WhenBalanceIsPositive_ThenFormatsWithPlusSign()
        {
            var localization = CreateLocalizationStub();
            var statisticsService = Substitute.For<IPlayerStatisticsService>();
            statisticsService.GetEntriesSnapshot().Returns(new[]
            {
                new StatisticsEntry(
                    new MatchKey("tic-tac-toe", StatisticsOpponentType.HotSeat, null),
                    new StatisticsRecord(3, 1, 0)),
            });

            var strategy = CreateStrategy("tic-tac-toe", 10, "Game.TicTacToe", Array.Empty<string>());
            var catalog = CreateCatalog(strategy);
            var botCatalog = CreateBotCatalog();

            var sut = new PlayerStatisticsViewModel(statisticsService, catalog, botCatalog, localization);
            sut.Initialize();

            var row = sut.Groups.CurrentValue[0].Rows[0];
            row.BalanceText.Should().Be("+2");
        }

        [Test]
        public void WhenBalanceIsNegative_ThenFormatsWithHyphenMinus()
        {
            var localization = CreateLocalizationStub();
            var statisticsService = Substitute.For<IPlayerStatisticsService>();
            statisticsService.GetEntriesSnapshot().Returns(new[]
            {
                new StatisticsEntry(
                    new MatchKey("tic-tac-toe", StatisticsOpponentType.HotSeat, null),
                    new StatisticsRecord(1, 3, 0)),
            });

            var strategy = CreateStrategy("tic-tac-toe", 10, "Game.TicTacToe", Array.Empty<string>());
            var catalog = CreateCatalog(strategy);
            var botCatalog = CreateBotCatalog();

            var sut = new PlayerStatisticsViewModel(statisticsService, catalog, botCatalog, localization);
            sut.Initialize();

            var row = sut.Groups.CurrentValue[0].Rows[0];
            row.BalanceText.Should().Be("-2");
        }

        [Test]
        public void WhenWinRateIsMidpointPointFive_ThenRoundsAwayFromZero()
        {
            var localization = CreateLocalizationStub();
            var statisticsService = Substitute.For<IPlayerStatisticsService>();
            statisticsService.GetEntriesSnapshot().Returns(new[]
            {
                new StatisticsEntry(
                    new MatchKey("tic-tac-toe", StatisticsOpponentType.HotSeat, null),
                    new StatisticsRecord(1, 7, 0)),
            });

            var strategy = CreateStrategy("tic-tac-toe", 10, "Game.TicTacToe", Array.Empty<string>());
            var catalog = CreateCatalog(strategy);
            var botCatalog = CreateBotCatalog();

            var sut = new PlayerStatisticsViewModel(statisticsService, catalog, botCatalog, localization);
            sut.Initialize();

            var row = sut.Groups.CurrentValue[0].Rows[0];
            row.WinRatePercent.Should().Be(13);
        }

        [Test]
        public void WhenMultipleGamesExist_ThenGroupsPreserveCatalogStrategyOrder()
        {
            var localization = CreateLocalizationStub();
            var statisticsService = Substitute.For<IPlayerStatisticsService>();
            statisticsService.GetEntriesSnapshot().Returns(new[]
            {
                new StatisticsEntry(new MatchKey("ttt", StatisticsOpponentType.HotSeat, null), new StatisticsRecord(1, 0, 0)),
                new StatisticsEntry(new MatchKey("chess", StatisticsOpponentType.HotSeat, null), new StatisticsRecord(1, 0, 0)),
            });

            var chess = CreateStrategy("chess", 1, "Game.Chess", Array.Empty<string>());
            var ttt = CreateStrategy("ttt", 2, "Game.TicTacToe", Array.Empty<string>());
            var catalog = CreateCatalog(chess, ttt);
            var botCatalog = CreateBotCatalog();

            var sut = new PlayerStatisticsViewModel(statisticsService, catalog, botCatalog, localization);
            sut.Initialize();

            sut.Groups.CurrentValue.Select(x => x.GameId).Should().Equal("chess", "ttt");
        }

        [Test]
        public void WhenRequestBackCalled_ThenPublishesBackRequested()
        {
            var localization = CreateLocalizationStub();
            var statisticsService = Substitute.For<IPlayerStatisticsService>();
            statisticsService.GetEntriesSnapshot().Returns(Array.Empty<StatisticsEntry>());
            var strategy = CreateStrategy("ttt", 1, "Game.TicTacToe", Array.Empty<string>());
            var catalog = CreateCatalog(strategy);
            var botCatalog = CreateBotCatalog();

            var sut = new PlayerStatisticsViewModel(statisticsService, catalog, botCatalog, localization);
            sut.Initialize();

            var calls = 0;
            using var subscription = sut.BackRequested.Subscribe(_ => calls++);

            sut.RequestBack();

            calls.Should().Be(1);
        }

        [Test]
        public void WhenLocalizationResolveReturnsPlaceholderOrSameKey_ThenUsesFallback()
        {
            var localization = Substitute.For<ILocalizationService>();
            localization.CurrentLocale.Returns(new ReactiveProperty<LocaleId>(LocaleId.EnglishUs));
            localization.Observe(Arg.Any<TextTableId>(), Arg.Any<TextKey>(), Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns(call => Observable.Return(call.Arg<TextKey>().Value));

            localization.Resolve(Arg.Any<TextTableId>(), Arg.Any<TextKey>(), Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns(call => call.Arg<TextKey>().Value switch
                {
                    "Game.GameA" => string.Empty,
                    "Game.GameB" => "Game.GameB",
                    "Game.GameC" => "[[missing]]",
                    "GameWizard.MatchSetup.HumanSettings.Local" => "Local",
                    var key => key,
                });

            var statisticsService = Substitute.For<IPlayerStatisticsService>();
            statisticsService.GetEntriesSnapshot().Returns(new[]
            {
                new StatisticsEntry(new MatchKey("game-a", StatisticsOpponentType.HotSeat, null), new StatisticsRecord(1, 0, 0)),
                new StatisticsEntry(new MatchKey("game-b", StatisticsOpponentType.HotSeat, null), new StatisticsRecord(1, 0, 0)),
                new StatisticsEntry(new MatchKey("game-c", StatisticsOpponentType.HotSeat, null), new StatisticsRecord(1, 0, 0)),
            });

            var catalog = CreateCatalog(
                CreateStrategy("game-a", 0, "Game.GameA", Array.Empty<string>()),
                CreateStrategy("game-b", 1, "Game.GameB", Array.Empty<string>()),
                CreateStrategy("game-c", 2, "Game.GameC", Array.Empty<string>()));

            var sut = new PlayerStatisticsViewModel(statisticsService, catalog, CreateBotCatalog(), localization);
            sut.Initialize();

            var titles = sut.Groups.CurrentValue.Select(x => x.GameTitle).ToArray();
            titles.Should().Equal("game-a", "game-b", "game-c");
            titles.Should().OnlyContain(x => !string.IsNullOrWhiteSpace(x));
            titles.Should().NotContain("Game.GameB");
            titles.Should().NotContain(x => x.Contains("[[", StringComparison.Ordinal));
        }

        private static ILocalizationService CreateLocalizationStub()
        {
            var localization = Substitute.For<ILocalizationService>();
            localization.CurrentLocale.Returns(new ReactiveProperty<LocaleId>(LocaleId.EnglishUs));

            localization.Observe(Arg.Any<TextTableId>(), Arg.Any<TextKey>(), Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns(call => Observable.Return(call.Arg<TextKey>().Value));

            localization.Resolve(Arg.Any<TextTableId>(), Arg.Any<TextKey>(), Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns(call => call.Arg<TextKey>().Value switch
                {
                    "Game.TicTacToe" => "Tic-Tac-Toe",
                    "GameWizard.MatchSetup.HumanSettings.Local" => "Local",
                    "GameWizard.MatchSetup.Opponent.Bot" => "Bot",
                    "PlayerStatistics.Opponent.Online" => "Online",
                    "GameWizard.MatchSetup.BotDifficulty.Easy" => "Easy",
                    "GameWizard.MatchSetup.BotDifficulty.Normal" => "Normal",
                    "GameWizard.MatchSetup.BotDifficulty.Hard" => "Hard",
                    var key => key,
                });

            return localization;
        }

        private static IGameCatalog CreateCatalog(params IGameStrategy[] strategies)
        {
            var catalog = Substitute.For<IGameCatalog>();
            catalog.Strategies.Returns(strategies);
            return catalog;
        }

        private static IBotDifficultyCatalog CreateBotCatalog()
        {
            var catalog = Substitute.For<IBotDifficultyCatalog>();
            catalog.Difficulties.Returns(new[]
            {
                new BotDifficulty("Easy", "GameWizard.MatchSetup.BotDifficulty.Easy", 0),
                new BotDifficulty("Normal", "GameWizard.MatchSetup.BotDifficulty.Normal", 1),
                new BotDifficulty("Hard", "GameWizard.MatchSetup.BotDifficulty.Hard", 2),
            });
            return catalog;
        }

        private static IGameStrategy CreateStrategy(string gameId, int order, string displayNameKey, IEnumerable<string> supportedDifficulties)
        {
            var strategy = Substitute.For<IGameStrategy>();
            strategy.GameId.Returns(gameId);
            strategy.Metadata.Returns(new GameMetadata(
                id: gameId,
                displayNameKey: displayNameKey,
                descriptionKey: "Game.Description",
                iconAssetKey: "icons/game",
                sortOrder: order,
                supportsBot: true,
                supportsOnline: true,
                supportsLocal: true));
            strategy.GetSupportedBotDifficultyIds().Returns(supportedDifficulties);
            return strategy;
        }
    }
}
