#nullable enable

using System;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.Modes;

namespace Runtime.GameModes.Wizard.Online
{
    public readonly struct OnlineMatchConfigPayload
    {
        public string GameId { get; }
        public int BoardSize { get; }
        public bool IsUltimate { get; }
        public int MoveTimeLimitSeconds { get; }
        public int PlacementTimeLimitSeconds { get; }

        public OnlineMatchConfigPayload(
            string gameId,
            int boardSize,
            bool isUltimate,
            int moveTimeLimitSeconds,
            int placementTimeLimitSeconds = 0)
        {
            if (string.IsNullOrWhiteSpace(gameId))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(gameId));

            if (boardSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(boardSize), boardSize, "BoardSize must be positive.");

            if (moveTimeLimitSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(moveTimeLimitSeconds), moveTimeLimitSeconds, "Value cannot be negative.");

            if (placementTimeLimitSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(placementTimeLimitSeconds), placementTimeLimitSeconds, "Value cannot be negative.");

            GameId = gameId;
            BoardSize = boardSize;
            IsUltimate = isUltimate;
            MoveTimeLimitSeconds = moveTimeLimitSeconds;
            PlacementTimeLimitSeconds = placementTimeLimitSeconds;
        }

        public static bool TryFromLaunchConfig(GameLaunchConfig? config, out OnlineMatchConfigPayload payload)
        {
            payload = default;
            
            if (config == null)
                return false;

            if (config.GameConfig is UltimateTicTacToeConfig)
            {
                payload = new OnlineMatchConfigPayload(config.GameId, boardSize: 3, isUltimate: true, moveTimeLimitSeconds: config.MoveTimeLimitSeconds);
                return true;
            }

            if (config.GameConfig is TicTacToeConfig ticTacToeConfig)
            {
                payload = new OnlineMatchConfigPayload(config.GameId, ticTacToeConfig.BoardSize, ticTacToeConfig.IsUltimate, config.MoveTimeLimitSeconds);
                return true;
            }

            if (config.GameConfig is BattleshipConfig battleshipConfig)
            {
                payload = new OnlineMatchConfigPayload(
                    config.GameId,
                    boardSize: 10,
                    isUltimate: false,
                    moveTimeLimitSeconds: config.MoveTimeLimitSeconds,
                    placementTimeLimitSeconds: battleshipConfig.PlacementTimeLimitSeconds);

                return true;
            }

            return false;
        }

        public IGameConfig ToGameConfig()
        {
            if (string.Equals(GameId, BattleshipStrategy.DefaultGameId, StringComparison.Ordinal))
                return new BattleshipConfig(PlacementTimeLimitSeconds);

            return IsUltimate
                ? UltimateTicTacToeConfig.Instance
                : new TicTacToeConfig(BoardSize, isUltimate: false);
        }
    }

    public readonly struct OnlineGameplaySessionSnapshot
    {
        public bool IsOnlineDirectInvite { get; }
        public string? SessionId { get; }
        public string? LocalUserId { get; }
        public bool IsHost { get; }
        public OnlineMatchConfigPayload? MatchConfig { get; }

        public OnlineGameplaySessionSnapshot(
            bool isOnlineDirectInvite,
            string? sessionId,
            string? localUserId,
            bool isHost,
            OnlineMatchConfigPayload? matchConfig)
        {
            IsOnlineDirectInvite = isOnlineDirectInvite;
            SessionId = sessionId;
            LocalUserId = localUserId;
            IsHost = isHost;
            MatchConfig = matchConfig;
        }

        public static OnlineGameplaySessionSnapshot Empty() => new(false, null, null, false, null);
    }

    public interface IOnlineGameplaySessionContextStore
    {
        OnlineGameplaySessionSnapshot Snapshot { get; }
        void SetOnlineSession(string sessionId, string localUserId, bool isHost);
        void SetDirectInviteSession(string sessionId, string localUserId, bool isHost);
        void SetMatchConfig(OnlineMatchConfigPayload matchConfig);
        void Clear();
    }

    public sealed class OnlineGameplaySessionContextStore : IOnlineGameplaySessionContextStore
    {
        private readonly object _gate = new();
        private OnlineGameplaySessionSnapshot _snapshot = OnlineGameplaySessionSnapshot.Empty();

        public OnlineGameplaySessionSnapshot Snapshot
        {
            get
            {
                lock (_gate)
                {
                    return _snapshot;
                }
            }
        }

        public void SetDirectInviteSession(string sessionId, string localUserId, bool isHost)
        {
            if (!OnlineSessionIdFormatter.TryNormalizeToCanonical(sessionId, out var canonicalSessionId))
                throw new ArgumentException("SessionId must be a valid canonical invite code.", nameof(sessionId));

            if (string.IsNullOrWhiteSpace(localUserId))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(localUserId));

            lock (_gate)
            {
                _snapshot = new OnlineGameplaySessionSnapshot(
                    isOnlineDirectInvite: true,
                    sessionId: canonicalSessionId,
                    localUserId: localUserId,
                    isHost: isHost,
                    matchConfig: null);
            }
        }

        public void SetOnlineSession(string sessionId, string localUserId, bool isHost)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(sessionId));

            if (string.IsNullOrWhiteSpace(localUserId))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(localUserId));

            lock (_gate)
            {
                _snapshot = new OnlineGameplaySessionSnapshot(
                    isOnlineDirectInvite: true,
                    sessionId: sessionId,
                    localUserId: localUserId,
                    isHost: isHost,
                    matchConfig: null);
            }
        }

        public void SetMatchConfig(OnlineMatchConfigPayload matchConfig)
        {
            lock (_gate)
            {
                if (!_snapshot.IsOnlineDirectInvite)
                    return;

                _snapshot = new OnlineGameplaySessionSnapshot(
                    isOnlineDirectInvite: _snapshot.IsOnlineDirectInvite,
                    sessionId: _snapshot.SessionId,
                    localUserId: _snapshot.LocalUserId,
                    isHost: _snapshot.IsHost,
                    matchConfig: matchConfig);
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                _snapshot = OnlineGameplaySessionSnapshot.Empty();
            }
        }
    }
}