#nullable enable

using System;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.Games.Battleship.Core;

namespace Runtime.Games.Battleship.Networking
{
    public readonly struct BattleshipPlacementMessage
    {
        public Guid CommandId { get; }
        public string SenderUserId { get; }
        public string LayoutPayload { get; }
        public long ClientTick { get; }

        public BattleshipPlacementMessage(Guid commandId, string senderUserId, string layoutPayload, long clientTick)
        {
            if (commandId == Guid.Empty)
                throw new ArgumentException("Value cannot be an empty GUID.", nameof(commandId));

            if (string.IsNullOrWhiteSpace(senderUserId))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(senderUserId));

            if (string.IsNullOrWhiteSpace(layoutPayload))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(layoutPayload));

            CommandId = commandId;
            SenderUserId = senderUserId;
            LayoutPayload = layoutPayload;
            ClientTick = clientTick;
        }
    }

    public readonly struct BattleshipPlacementTimeoutMessage
    {
        public Guid CommandId { get; }
        public string SenderUserId { get; }
        public int PlayerSlot { get; }
        public int AutoPlaceSeed { get; }
        public long ClientTick { get; }

        public BattleshipPlacementTimeoutMessage(
            Guid commandId,
            string senderUserId,
            int playerSlot,
            int autoPlaceSeed,
            long clientTick)
        {
            if (commandId == Guid.Empty)
                throw new ArgumentException("Value cannot be an empty GUID.", nameof(commandId));

            if (string.IsNullOrWhiteSpace(senderUserId))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(senderUserId));

            if (playerSlot < 0)
                throw new ArgumentOutOfRangeException(nameof(playerSlot), playerSlot, "Value cannot be negative.");

            CommandId = commandId;
            SenderUserId = senderUserId;
            PlayerSlot = playerSlot;
            AutoPlaceSeed = autoPlaceSeed;
            ClientTick = clientTick;
        }
    }

    public readonly struct BattleshipRecoveryMessage
    {
        public Guid CommandId { get; }
        public string SenderUserId { get; }
        public int MatchRoundId { get; }
        public int Phase { get; }
        public int ActivePlayerSlot { get; }
        public long PlacementTimerRemainingMs { get; }
        public long MoveTimerRemainingMs { get; }
        public int Player0ConsecutiveTimeouts { get; }
        public int Player1ConsecutiveTimeouts { get; }
        public int WinnerSlot { get; }
        public int FinishStatus { get; }
        public long ClientTick { get; }
        public string Player0LayoutPayload { get; }
        public string Player1LayoutPayload { get; }
        public string Player0OpponentMarksPayload { get; }
        public string Player1OpponentMarksPayload { get; }

        public BattleshipRecoveryMessage(
            Guid commandId,
            string senderUserId,
            int matchRoundId,
            int phase,
            int activePlayerSlot,
            long placementTimerRemainingMs,
            long moveTimerRemainingMs,
            int player0ConsecutiveTimeouts,
            int player1ConsecutiveTimeouts,
            int winnerSlot,
            int finishStatus,
            long clientTick,
            string? player0LayoutPayload,
            string? player1LayoutPayload,
            string? player0OpponentMarksPayload,
            string? player1OpponentMarksPayload)
        {
            if (commandId == Guid.Empty)
                throw new ArgumentException("Value cannot be an empty GUID.", nameof(commandId));

            if (string.IsNullOrWhiteSpace(senderUserId))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(senderUserId));

            if (matchRoundId < 1)
                throw new ArgumentOutOfRangeException(nameof(matchRoundId), matchRoundId, "Value must be at least 1.");

            CommandId = commandId;
            SenderUserId = senderUserId;
            MatchRoundId = matchRoundId;
            Phase = phase;
            ActivePlayerSlot = activePlayerSlot;
            PlacementTimerRemainingMs = placementTimerRemainingMs;
            MoveTimerRemainingMs = moveTimerRemainingMs;
            Player0ConsecutiveTimeouts = player0ConsecutiveTimeouts;
            Player1ConsecutiveTimeouts = player1ConsecutiveTimeouts;
            WinnerSlot = winnerSlot;
            FinishStatus = finishStatus;
            ClientTick = clientTick;
            Player0LayoutPayload = player0LayoutPayload ?? string.Empty;
            Player1LayoutPayload = player1LayoutPayload ?? string.Empty;
            Player0OpponentMarksPayload = player0OpponentMarksPayload ?? string.Empty;
            Player1OpponentMarksPayload = player1OpponentMarksPayload ?? string.Empty;
        }
    }

    public interface IBattleshipLayoutSerializer
    {
        string Serialize(FleetLayout layout);
        bool TryDeserialize(string payload, out FleetLayout layout);
    }

    public interface IBattleshipNetworkBridge : IDisposable
    {
        Observable<BattleshipPlacementMessage> IncomingPlacements { get; }
        Observable<BattleshipPlacementTimeoutMessage> IncomingPlacementTimeouts { get; }
        Observable<BattleshipRecoveryMessage> IncomingRecoverySnapshots { get; }

        UniTask BindAsync(string localUserId, bool isHost);
        UniTask UnbindAsync();

        UniTask SubmitPlacementAsync(BattleshipPlacementMessage message);
        UniTask SubmitPlacementTimeoutAsync(BattleshipPlacementTimeoutMessage message);
        UniTask SubmitRecoverySnapshotAsync(BattleshipRecoveryMessage message);
    }
}