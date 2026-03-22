using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using NUnit.Framework;
using Runtime.Infrastructure.Save;
using Runtime.PlayerStatistics;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.EditMode.PlayerStatistics
{
    [TestFixture]
    [Category("Unit")]
    public sealed class PlayerStatisticsServiceTests
    {
        [Test]
        public void WhenRecordMatchCalled_ThenIncrementsMatchingCounter()
        {
            var storage = new InMemorySaveStorage();
            var sut = new PlayerStatisticsService(storage, storage);
            sut.Initialize();

            var key = new MatchKey("tic-tac-toe", StatisticsOpponentType.HotSeat, null);

            sut.RecordMatch(key, MatchOutcome.Win);
            sut.RecordMatch(key, MatchOutcome.Draw);
            sut.RecordMatch(key, MatchOutcome.Loss);

            var snapshot = sut.GetEntriesSnapshot();
            snapshot.Should().HaveCount(1);
            snapshot[0].Record.Wins.Should().Be(1);
            snapshot[0].Record.Losses.Should().Be(1);
            snapshot[0].Record.Draws.Should().Be(1);
        }

        [Test]
        public void WhenSaveAndLoadRoundTrip_ThenKeepsInsertionOrder()
        {
            var storage = new InMemorySaveStorage();
            var writer = new PlayerStatisticsService(storage, storage);
            writer.Initialize();

            writer.RecordMatch(new MatchKey("tic-tac-toe", StatisticsOpponentType.HotSeat, null), MatchOutcome.Win);
            writer.RecordMatch(new MatchKey("ultimate-tic-tac-toe", StatisticsOpponentType.Online, null), MatchOutcome.Loss);
            writer.RecordMatch(new MatchKey("tic-tac-toe", StatisticsOpponentType.Bot, "Hard"), MatchOutcome.Draw);

            var reader = new PlayerStatisticsService(storage, storage);
            reader.Initialize();

            var snapshot = reader.GetEntriesSnapshot();
            snapshot.Should().HaveCount(3);
            snapshot[0].Key.GameId.Should().Be("tic-tac-toe");
            snapshot[0].Key.OpponentType.Should().Be(StatisticsOpponentType.HotSeat);
            snapshot[1].Key.GameId.Should().Be("ultimate-tic-tac-toe");
            snapshot[1].Key.OpponentType.Should().Be(StatisticsOpponentType.Online);
            snapshot[2].Key.GameId.Should().Be("tic-tac-toe");
            snapshot[2].Key.OpponentType.Should().Be(StatisticsOpponentType.Bot);
            snapshot[2].Key.BotDifficultyId.Should().Be("Hard");
        }

        [Test]
        public void WhenLoadContainsInvalidEntries_ThenSkipsInvalidAndNegativeCounters()
        {
            var storage = new InMemorySaveStorage();
            
            storage.Seed(
                "player_statistics",
                new[]
                {
                    new StatisticsEntryDto
                    {
                        gameId = "tic-tac-toe",
                        opponentType = "HotSeat",
                        botDifficultyId = null,
                        wins = 1,
                        losses = 2,
                        draws = 3,
                    },
                    new StatisticsEntryDto
                    {
                        gameId = "tic-tac-toe",
                        opponentType = "Unknown",
                        botDifficultyId = null,
                        wins = 1,
                        losses = 0,
                        draws = 0,
                    },
                    new StatisticsEntryDto
                    {
                        gameId = "ultimate",
                        opponentType = "HotSeat",
                        botDifficultyId = "Hard",
                        wins = 1,
                        losses = 0,
                        draws = 0,
                    },
                    new StatisticsEntryDto
                    {
                        gameId = "tic-tac-toe",
                        opponentType = "Bot",
                        botDifficultyId = "",
                        wins = 1,
                        losses = 0,
                        draws = 0,
                    },
                    new StatisticsEntryDto
                    {
                        gameId = "tic-tac-toe",
                        opponentType = "Online",
                        botDifficultyId = null,
                        wins = -1,
                        losses = 0,
                        draws = 0,
                    },
                });

            var sut = new PlayerStatisticsService(storage, storage);
            sut.Initialize();

            var snapshot = sut.GetEntriesSnapshot();
            snapshot.Should().HaveCount(1);
            snapshot[0].Record.Wins.Should().Be(1);
            snapshot[0].Record.Losses.Should().Be(2);
            snapshot[0].Record.Draws.Should().Be(3);
        }

        [Test]
        public void WhenLoadContainsUnknownIds_ThenUnknownEntriesSurviveRoundTrip()
        {
            var storage = new InMemorySaveStorage();
            
            storage.Seed(
                "player_statistics",
                new[]
                {
                    new StatisticsEntryDto
                    {
                        gameId = "removed-game",
                        opponentType = "Bot",
                        botDifficultyId = "Nightmare",
                        wins = 4,
                        losses = 1,
                        draws = 0,
                    },
                });

            var sut = new PlayerStatisticsService(storage, storage);
            sut.Initialize();

            sut.RecordMatch(new MatchKey("tic-tac-toe", StatisticsOpponentType.HotSeat, null), MatchOutcome.Win);

            var reloaded = new PlayerStatisticsService(storage, storage);
            reloaded.Initialize();
            var snapshot = reloaded.GetEntriesSnapshot();

            snapshot.Should().HaveCount(2);
            snapshot[0].Key.GameId.Should().Be("removed-game");
            snapshot[0].Key.BotDifficultyId.Should().Be("Nightmare");
        }

        [Test]
        public void WhenRecordMatchBeforeInitialize_ThenLogsErrorAndKeepsEmptySnapshot()
        {
            var storage = new InMemorySaveStorage();
            var sut = new PlayerStatisticsService(storage, storage);

            LogAssert.Expect(LogType.Error, "[Core] [PlayerStatisticsService] RecordMatch called before Initialize. Entry dropped.");
            sut.RecordMatch(new MatchKey("tic-tac-toe", StatisticsOpponentType.HotSeat, null), MatchOutcome.Win);

            sut.GetEntriesSnapshot().Should().BeEmpty();
        }

        [Test]
        public void WhenLoadContainsDuplicateKeys_ThenFirstWinsAndLogsWarning()
        {
            var storage = new InMemorySaveStorage();
            
            storage.Seed(
                "player_statistics",
                new[]
                {
                    new StatisticsEntryDto
                    {
                        gameId = "ttt",
                        opponentType = "HotSeat",
                        botDifficultyId = null,
                        wins = 1,
                        losses = 0,
                        draws = 0,
                    },
                    new StatisticsEntryDto
                    {
                        gameId = "ttt",
                        opponentType = "HotSeat",
                        botDifficultyId = null,
                        wins = 5,
                        losses = 0,
                        draws = 0,
                    },
                });

            LogAssert.Expect(LogType.Warning, new Regex(@"^\[Core\] \[PlayerStatisticsService\] Duplicate statistics entry ignored \(first-wins\)"));
            var sut = new PlayerStatisticsService(storage, storage);

            sut.Initialize();

            var snapshot = sut.GetEntriesSnapshot();
            snapshot.Should().HaveCount(1);
            snapshot[0].Record.Wins.Should().Be(1);
        }

        [Test]
        public void WhenSaveResultIsFailure_ThenInMemoryStateSurvivesAndErrorIsLogged()
        {
            var storage = new SaveFailureStorage();
            var sut = new PlayerStatisticsService(storage, storage);
            sut.Initialize();

            LogAssert.Expect(LogType.Error, new Regex(@"^\[Core\] \[PlayerStatisticsService\] Failed to save statistics\. SaveError=BackendWriteFailed$"));
            sut.RecordMatch(new MatchKey("ttt", StatisticsOpponentType.HotSeat, null), MatchOutcome.Win);

            var snapshot = sut.GetEntriesSnapshot();
            snapshot.Should().HaveCount(1);
            snapshot[0].Record.Wins.Should().Be(1);
        }

        [Test]
        public void WhenLoadThrowsException_ThenLogsErrorAndKeepsEmptyState()
        {
            var storage = new ThrowingLoadStorage();
            var sut = new PlayerStatisticsService(storage, storage);

            LogAssert.Expect(LogType.Error, new Regex(@"^\[Core\] \[PlayerStatisticsService\] Failed to load statistics\. Using empty state\. Error="));

            sut.Initialize();

            sut.GetEntriesSnapshot().Should().BeEmpty();
        }

        [Test]
        public void WhenTrySaveThrowsException_ThenLogsErrorAndInMemoryStateSurvives()
        {
            var storage = new ThrowingTrySaveStorage();
            var sut = new PlayerStatisticsService(storage, storage);
            sut.Initialize();

            LogAssert.Expect(LogType.Error, new Regex(@"^\[Core\] \[PlayerStatisticsService\] Failed to save statistics\. Error="));
            sut.RecordMatch(new MatchKey("ttt", StatisticsOpponentType.HotSeat, null), MatchOutcome.Win);

            var snapshot = sut.GetEntriesSnapshot();
            snapshot.Should().HaveCount(1);
            snapshot[0].Record.Wins.Should().Be(1);
        }

        [Test]
        public void WhenLoadedArrayContainsNullDto_ThenSkipsAndLogsWarning()
        {
            var storage = new InMemorySaveStorage();
           
            storage.Seed(
                "player_statistics",
                new[]
                {
                    null,
                    new StatisticsEntryDto
                    {
                        gameId = "ttt",
                        opponentType = "HotSeat",
                        botDifficultyId = null,
                        wins = 2,
                        losses = 1,
                        draws = 0,
                    },
                });

            LogAssert.Expect(LogType.Warning, new Regex(@"^\[Core\] \[PlayerStatisticsService\] Ignored null statistics DTO during load\.$"));
            var sut = new PlayerStatisticsService(storage, storage);

            sut.Initialize();

            var snapshot = sut.GetEntriesSnapshot();
            snapshot.Should().HaveCount(1);
            snapshot[0].Record.Wins.Should().Be(2);
        }

        [Test]
        public void WhenOpponentTypeHasWrongCase_ThenEntryIsIgnored()
        {
            var storage = new InMemorySaveStorage();
            
            storage.Seed(
                "player_statistics",
                new[]
                {
                    new StatisticsEntryDto
                    {
                        gameId = "ttt",
                        opponentType = "hotseat",
                        botDifficultyId = null,
                        wins = 3,
                        losses = 0,
                        draws = 0,
                    },
                });

            LogAssert.Expect(LogType.Warning, new Regex(@"^\[Core\] \[PlayerStatisticsService\] Ignored statistics entry with unsupported opponentType='hotseat'\.$"));
            var sut = new PlayerStatisticsService(storage, storage);

            sut.Initialize();

            sut.GetEntriesSnapshot().Should().BeEmpty();
        }

        private sealed class InMemorySaveStorage : ISaveService, ISaveServiceWithResult
        {
            private readonly Dictionary<string, object> _sections = new(StringComparer.Ordinal);

            public T Load<T>(string section, T defaultValue)
            {
                if (_sections.TryGetValue(section, out var value) && value is T typed)
                    return typed;

                return defaultValue;
            }

            public SaveWriteResult TrySave<T>(string section, T data)
            {
                _sections[section] = data;
                return SaveWriteResult.Success();
            }

            public void Save<T>(string section, T data) => _sections[section] = data;

            public void Seed<T>(string section, T data) => _sections[section] = data;
        }

        private sealed class SaveFailureStorage : ISaveService, ISaveServiceWithResult
        {
            private readonly Dictionary<string, object> _sections = new(StringComparer.Ordinal);

            public T Load<T>(string section, T defaultValue)
            {
                if (_sections.TryGetValue(section, out var value) && value is T typed)
                    return typed;

                return defaultValue;
            }

            public SaveWriteResult TrySave<T>(string section, T data)
            {
                _sections[section] = data;
                return SaveWriteResult.Failed(SaveWriteError.BackendWriteFailed);
            }

            public void Save<T>(string section, T data) => _sections[section] = data;
        }

        private sealed class ThrowingLoadStorage : ISaveService, ISaveServiceWithResult
        {
            public T Load<T>(string section, T defaultValue) => throw new IOException("load failed");

            public SaveWriteResult TrySave<T>(string section, T data) => SaveWriteResult.Success();

            public void Save<T>(string section, T data) { }
        }

        private sealed class ThrowingTrySaveStorage : ISaveService, ISaveServiceWithResult
        {
            public T Load<T>(string section, T defaultValue) => defaultValue;

            public SaveWriteResult TrySave<T>(string section, T data) => throw new IOException("save failed");

            public void Save<T>(string section, T data) { }
        }
    }
}