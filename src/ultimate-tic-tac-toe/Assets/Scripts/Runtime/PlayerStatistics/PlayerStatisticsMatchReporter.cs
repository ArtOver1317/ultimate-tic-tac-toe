#nullable enable

using System;
using R3;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.Matchmaking.Config;
using Runtime.GameModes.Wizard.Online;
using Runtime.Gameplay;
using Runtime.Infrastructure.Logging;

namespace Runtime.PlayerStatistics
{
    public sealed class PlayerStatisticsMatchReporter : IDisposable
    {
        private readonly CompositeDisposable _disposables = new();

        public PlayerStatisticsMatchReporter(
            IGameLaunchConfigStore configStore,
            IGameplayEventStream eventStream,
            IMatchOutcomeResolver outcomeResolver,
            IPlayerStatisticsService statisticsService,
            IOnlineGameplaySessionContextStore contextStore,
            MatchKeyMapper keyMapper)
        {
            if (configStore == null)
                throw new ArgumentNullException(nameof(configStore));

            if (eventStream == null)
                throw new ArgumentNullException(nameof(eventStream));

            if (outcomeResolver == null)
                throw new ArgumentNullException(nameof(outcomeResolver));

            if (statisticsService == null)
                throw new ArgumentNullException(nameof(statisticsService));

            if (contextStore == null)
                throw new ArgumentNullException(nameof(contextStore));

            if (keyMapper == null)
                throw new ArgumentNullException(nameof(keyMapper));

            if (!configStore.TryPeek(out var config) || config == null)
                throw new InvalidOperationException("PlayerStatisticsMatchReporter requires launch config to be present in IGameLaunchConfigStore before resolve.");

            if (!keyMapper.TryMap(config, out var mappedKey))
            {
                GameLog.Warning($"[PlayerStatisticsMatchReporter] Unsupported opponent config '{config.OpponentConfig?.GetType().Name ?? "<null>"}'. Statistics reporting disabled for this match.");
                return;
            }

            if (!TryResolveLocalHostFlag(contextStore.Snapshot, mappedKey.OpponentType, out var isLocalPlayerHost))
            {
                var onlineContextKind = ResolveOnlineContextKind(config.OpponentConfig);
                GameLog.Warning($"[PlayerStatisticsMatchReporter] Online match without reliable local slot context (kind='{onlineContextKind}'). Statistics reporting disabled for this match.");
                return;
            }
            
            eventStream.RoundFinished
                .Subscribe(evt =>
                {
                    if (!outcomeResolver.TryResolveOutcome(evt, mappedKey.OpponentType, isLocalPlayerHost, out var outcome))
                        return;

                    statisticsService.RecordMatch(mappedKey, outcome);
                })
                .AddTo(_disposables);
        }

        private static bool TryResolveLocalHostFlag(OnlineGameplaySessionSnapshot snapshot, StatisticsOpponentType opponentType, out bool isLocalPlayerHost)
        {
            isLocalPlayerHost = true;

            if (opponentType != StatisticsOpponentType.Online)
                return true;

            if (snapshot.IsOnlineDirectInvite)
            {
                isLocalPlayerHost = snapshot.IsHost;
                return true;
            }

            return false;
        }

        private static string ResolveOnlineContextKind(IOpponentConfig? opponentConfig) =>
            opponentConfig switch
            {
                DirectInviteConfig => "direct-invite-without-session-context",
                MatchmakingConfig => "matchmaking",
                _ => opponentConfig?.GetType().Name ?? "<null>",
            };

        public void Dispose() => _disposables.Dispose();
    }
}