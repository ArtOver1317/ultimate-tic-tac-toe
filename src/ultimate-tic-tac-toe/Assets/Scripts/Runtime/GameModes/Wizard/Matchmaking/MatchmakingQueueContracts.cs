#nullable enable

using System;

namespace Runtime.GameModes.Wizard.Matchmaking
{
    public sealed class MatchmakingRoomOptions
    {
        public string Region { get; }
        public string GameId { get; }
        public string ParamsHash { get; }
        public int MaxPlayers { get; }

        public MatchmakingRoomOptions(string region, string gameId, string paramsHash, int maxPlayers = 2)
        {
            if (string.IsNullOrWhiteSpace(region))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(region));

            if (string.IsNullOrWhiteSpace(gameId))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(gameId));

            if (string.IsNullOrWhiteSpace(paramsHash))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(paramsHash));

            if (maxPlayers <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxPlayers), maxPlayers, "Value must be positive.");

            Region = region;
            GameId = gameId;
            ParamsHash = paramsHash;
            MaxPlayers = maxPlayers;
        }
    }

    public sealed class MatchmakingRoomResult
    {
        public string RoomName { get; }
        public int PlayersCount { get; }
        public string? OpponentId { get; }
        public bool IsHost { get; }

        public MatchmakingRoomResult(string roomName, int playersCount, string? opponentId, bool isHost)
        {
            if (string.IsNullOrWhiteSpace(roomName))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(roomName));

            if (playersCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(playersCount), playersCount, "Value must be positive.");

            RoomName = roomName;
            PlayersCount = playersCount;
            OpponentId = opponentId;
            IsHost = isHost;
        }
    }

    public sealed class QueueEntry
    {
        public string RoomName { get; }
        public MatchmakingResult? ImmediateResult { get; }
        public bool IsPaired => ImmediateResult != null;

        public QueueEntry(string roomName, MatchmakingResult? immediateResult)
        {
            if (string.IsNullOrWhiteSpace(roomName))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(roomName));

            RoomName = roomName;
            ImmediateResult = immediateResult;
        }
    }

    public interface IMatchmakingConfig
    {
        TimeSpan SearchTimeout { get; }
        TimeSpan CancelAckTimeout { get; }
    }

    public sealed class MatchmakingConfigDefaults : IMatchmakingConfig
    {
        public static readonly MatchmakingConfigDefaults Instance = new();

        public TimeSpan SearchTimeout => TimeSpan.FromSeconds(60);
        public TimeSpan CancelAckTimeout => TimeSpan.FromSeconds(15);
    }

    public sealed class MatchmakingCancelAckTimeoutException : TimeoutException
    {
        public MatchmakingCancelAckTimeoutException(string message)
            : base(message)
        {
        }
    }

    public sealed class ConnectionLostException : Exception
    {
        public ConnectionLostException(string message)
            : base(message)
        {
        }

        public ConnectionLostException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
