#nullable enable
using System;
using System.Collections.Generic;
using Runtime.GameModes.Wizard.Configs;
using Runtime.Gameplay;
using Runtime.Gameplay.Startup;
using Runtime.Games.TicTacToe.Series;

namespace Runtime.Games.Battleship.Startup
{
    internal sealed class GameplayStartupBattleshipSessionScoreStore
    {
        private static readonly object _gate = new();
        private static readonly Dictionary<string, SeriesScore> _scores = new(StringComparer.Ordinal);

        internal void RestoreIfNeeded(
            GameplayStartupDependencies services,
            GameplayStartupRuntimeState state,
            GameLaunchConfig config,
            Action updateScoreLabels)
        {
            if (!state.Battleship.IsBattleshipMatch)
                return;

            var key = BuildSessionKey(services, config);
            
            if (string.IsNullOrWhiteSpace(key))
                return;

            if (!config.StartingPlayerSlotOverride.HasValue)
            {
                lock (_gate)
                {
                    _scores.Remove(key);
                }

                return;
            }

            SeriesScore storedScore;
            
            lock (_gate)
            {
                if (!_scores.TryGetValue(key, out storedScore))
                    return;
            }

            for (var i = 0; i < storedScore.Player1Wins; i++)
            {
                services.Core.SeriesService.RecordResult(GameResult.Timeout(PlayerMark.X));
            }

            for (var i = 0; i < storedScore.Player2Wins; i++)
            {
                services.Core.SeriesService.RecordResult(GameResult.Timeout(PlayerMark.O));
            }

            for (var i = 0; i < storedScore.Draws; i++)
            {
                services.Core.SeriesService.RecordResult(GameResult.Draw());
            }

            for (var i = 0; i < storedScore.RoundIndex; i++)
            {
                services.Core.SeriesService.NextRound();
            }

            updateScoreLabels();
        }

        internal void PersistIfNeeded(GameplayStartupDependencies services, GameplayStartupRuntimeState state)
        {
            if (!state.Battleship.IsBattleshipMatch || state.Match.ActiveLaunchConfig == null)
                return;

            var key = BuildSessionKey(services, state.Match.ActiveLaunchConfig);
            
            if (string.IsNullOrWhiteSpace(key))
                return;

            lock (_gate)
            {
                _scores[key] = services.Core.SeriesService.Score.CurrentValue;
            }
        }

        private static string BuildSessionKey(GameplayStartupDependencies services, GameLaunchConfig config)
        {
            if (services.Online.OnlineSessionContextStore.Snapshot.IsOnlineDirectInvite)
            {
                var sessionId = services.Online.OnlineSessionContextStore.Snapshot.SessionId;

                if (!string.IsNullOrWhiteSpace(sessionId))
                    return $"online:{sessionId}";
            }

            return $"local:{config.GameId}";
        }
    }
}