#nullable enable

using System;
using System.Collections.Generic;
using Runtime.Infrastructure.Logging;
using Runtime.Infrastructure.Save;
using VContainer.Unity;

namespace Runtime.PlayerStatistics
{
    public sealed class PlayerStatisticsService : IPlayerStatisticsService, IInitializable
    {
        private const string _saveSection = "player_statistics";

        private readonly ISaveService _saveService;
        private readonly ISaveServiceWithResult _saveServiceWithResult;
        private static readonly IReadOnlyList<StatisticsEntry> _emptySnapshot = Array.AsReadOnly(Array.Empty<StatisticsEntry>());

        private readonly List<StatisticsEntry> _entries = new();
        private readonly Dictionary<MatchKey, int> _indexByKey = new();
        private IReadOnlyList<StatisticsEntry> _cachedSnapshot = _emptySnapshot;
        private bool _isSnapshotDirty = true;

        private bool _isInitialized;

        public PlayerStatisticsService(
            ISaveService saveService,
            ISaveServiceWithResult saveServiceWithResult)
        {
            _saveService = saveService ?? throw new ArgumentNullException(nameof(saveService));
            _saveServiceWithResult = saveServiceWithResult ?? throw new ArgumentNullException(nameof(saveServiceWithResult));
        }

        public void Initialize()
        {
            if (_isInitialized)
                return;

            _isInitialized = true;
            LoadFromSave();
        }

        public void RecordMatch(MatchKey key, MatchOutcome outcome)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            if (!_isInitialized)
            {
                GameLog.Error("[PlayerStatisticsService] RecordMatch called before Initialize. Entry dropped.");
                return;
            }

            if (_indexByKey.TryGetValue(key, out var existingIndex))
            {
                var existing = _entries[existingIndex];
                var updatedRecord = Increment(existing.Record, outcome);
                _entries[existingIndex] = new StatisticsEntry(existing.Key, updatedRecord);
            }
            else
            {
                var record = Increment(new StatisticsRecord(0, 0, 0), outcome);
                _indexByKey[key] = _entries.Count;
                _entries.Add(new StatisticsEntry(key, record));
            }

            _isSnapshotDirty = true;

            PersistSnapshot();
        }

        public IReadOnlyList<StatisticsEntry> GetEntriesSnapshot()
        {
            if (!_isInitialized)
                return _emptySnapshot;

            if (_isSnapshotDirty)
            {
                _cachedSnapshot = Array.AsReadOnly(_entries.ToArray());
                _isSnapshotDirty = false;
            }

            return _cachedSnapshot;
        }

        private void LoadFromSave()
        {
            StatisticsEntryDto[] loaded;
            
            try
            {
                loaded = _saveService.Load(_saveSection, Array.Empty<StatisticsEntryDto>());
            }
            catch (Exception ex)
            {
                GameLog.Error($"[PlayerStatisticsService] Failed to load statistics. Using empty state. Error={ex.Message}");
                return;
            }

            if (loaded == null || loaded.Length == 0)
                return;

            for (var i = 0; i < loaded.Length; i++)
            {
                var dto = loaded[i];

                if (!TryMapDto(dto, out var entry))
                    continue;

                if (_indexByKey.ContainsKey(entry.Key))
                {
                    GameLog.Warning($"[PlayerStatisticsService] Duplicate statistics entry ignored (first-wins). GameId='{entry.Key.GameId}', OpponentType='{entry.Key.OpponentType}', BotDifficultyId='{entry.Key.BotDifficultyId ?? "<null>"}'.");
                    continue;
                }

                _indexByKey[entry.Key] = _entries.Count;
                _entries.Add(entry);
            }

            _isSnapshotDirty = true;
        }

        private bool TryMapDto(StatisticsEntryDto? dto, out StatisticsEntry entry)
        {
            entry = null!;

            if (dto == null)
            {
                GameLog.Warning("[PlayerStatisticsService] Ignored null statistics DTO during load.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(dto.gameId))
            {
                GameLog.Warning("[PlayerStatisticsService] Ignored statistics entry with empty gameId.");
                return false;
            }

            if (!TryParseOpponentType(dto.opponentType, out var opponentType))
            {
                GameLog.Warning($"[PlayerStatisticsService] Ignored statistics entry with unsupported opponentType='{dto.opponentType ?? "<null>"}'.");
                return false;
            }

            if (!IsBotDifficultyShapeValid(opponentType, dto.botDifficultyId))
            {
                GameLog.Warning($"[PlayerStatisticsService] Ignored statistics entry with invalid botDifficultyId shape. OpponentType='{opponentType}', BotDifficultyId='{dto.botDifficultyId ?? "<null>"}'.");
                return false;
            }

            if (dto.wins < 0 || dto.losses < 0 || dto.draws < 0)
            {
                GameLog.Warning($"[PlayerStatisticsService] Ignored statistics entry with negative counters. W={dto.wins}, L={dto.losses}, D={dto.draws}.");
                return false;
            }

            MatchKey key;
            
            try
            {
                key = new MatchKey(dto.gameId, opponentType, dto.botDifficultyId);
            }
            catch (ArgumentException ex)
            {
                GameLog.Warning($"[PlayerStatisticsService] Ignored statistics entry due to invalid key data. Error={ex.Message}");
                return false;
            }

            var record = new StatisticsRecord(dto.wins, dto.losses, dto.draws);
            entry = new StatisticsEntry(key, record);
            return true;
        }

        private void PersistSnapshot()
        {
            var dto = new StatisticsEntryDto[_entries.Count];
            
            for (var i = 0; i < _entries.Count; i++)
            {
                dto[i] = ToDto(_entries[i]);
            }

            SaveWriteResult result;
           
            try
            {
                result = _saveServiceWithResult.TrySave(_saveSection, dto);
            }
            catch (Exception ex)
            {
                GameLog.Error($"[PlayerStatisticsService] Failed to save statistics. Error={ex.Message}");
                return;
            }

            if (!result.IsSuccess) 
                GameLog.Error($"[PlayerStatisticsService] Failed to save statistics. SaveError={result.Error}");
        }

        private static StatisticsEntryDto ToDto(StatisticsEntry entry) =>
            new()
            {
                gameId = entry.Key.GameId,
                opponentType = entry.Key.OpponentType.ToString(),
                botDifficultyId = entry.Key.BotDifficultyId,
                wins = entry.Record.Wins,
                losses = entry.Record.Losses,
                draws = entry.Record.Draws,
            };

        private static bool TryParseOpponentType(string? value, out StatisticsOpponentType opponentType)
        {
            if (!Enum.TryParse(value, ignoreCase: false, out opponentType))
                return false;

            return opponentType is StatisticsOpponentType.HotSeat
                or StatisticsOpponentType.Bot
                or StatisticsOpponentType.Online;
        }

        private static bool IsBotDifficultyShapeValid(StatisticsOpponentType opponentType, string? botDifficultyId)
        {
            if (opponentType == StatisticsOpponentType.Bot)
                return !string.IsNullOrWhiteSpace(botDifficultyId);

            return string.IsNullOrWhiteSpace(botDifficultyId);
        }

        private static StatisticsRecord Increment(StatisticsRecord source, MatchOutcome outcome) =>
            outcome switch
            {
                MatchOutcome.Win => new StatisticsRecord(source.Wins + 1, source.Losses, source.Draws),
                MatchOutcome.Loss => new StatisticsRecord(source.Wins, source.Losses + 1, source.Draws),
                MatchOutcome.Draw => new StatisticsRecord(source.Wins, source.Losses, source.Draws + 1),
                _ => source,
            };
    }
}