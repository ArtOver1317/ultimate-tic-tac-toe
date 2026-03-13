#nullable enable

using System;
using System.Globalization;
using System.Text;

namespace Runtime.Games.Battleship.Networking
{
    internal static class BattleshipReliablePayloadCodec
    {
        private const string _encodedFieldPrefix = "b64:";

        public static byte[] SerializePlacement(BattleshipPlacementMessage message)
        {
            var line = string.Concat(
                PlacementPayloadParts.Prefix, "|",
                message.CommandId.ToString("N"), "|",
                EncodeField(message.SenderUserId), "|",
                EncodeField(message.LayoutPayload), "|",
                message.ClientTick.ToString(CultureInfo.InvariantCulture));

            return Encoding.UTF8.GetBytes(line);
        }

        public static byte[] SerializePlacementTimeout(BattleshipPlacementTimeoutMessage message)
        {
            var line = string.Concat(
                PlacementTimeoutPayloadParts.Prefix, "|",
                message.CommandId.ToString("N"), "|",
                EncodeField(message.SenderUserId), "|",
                message.PlayerSlot.ToString(CultureInfo.InvariantCulture), "|",
                message.AutoPlaceSeed.ToString(CultureInfo.InvariantCulture), "|",
                message.ClientTick.ToString(CultureInfo.InvariantCulture));

            return Encoding.UTF8.GetBytes(line);
        }

        public static bool TryDeserializePlacement(byte[] payload, out BattleshipPlacementMessage message)
        {
            message = default;

            if (!TryGetPayloadParts(payload, PlacementPayloadParts.Prefix, PlacementPayloadParts.Count, out var parts))
                return false;

            if (!TryParseRequiredGuid(parts[CommonPayloadParts.CommandId], out var commandId))
                return false;

            if (!TryParseRequiredString(parts[CommonPayloadParts.SenderUserId], out var senderUserId))
                return false;

            if (!TryParseRequiredString(parts[PlacementPayloadParts.LayoutPayload], out var layoutPayload))
                return false;

            message = new BattleshipPlacementMessage(commandId, senderUserId, layoutPayload, ParseInt64OrDefault(parts[PlacementPayloadParts.ClientTick]));
            return true;
        }

        public static bool TryDeserializePlacementTimeout(byte[] payload, out BattleshipPlacementTimeoutMessage message)
        {
            message = default;

            if (!TryGetPayloadParts(payload, PlacementTimeoutPayloadParts.Prefix, PlacementTimeoutPayloadParts.Count, out var parts))
                return false;

            if (!TryParseRequiredGuid(parts[CommonPayloadParts.CommandId], out var commandId))
                return false;

            if (!TryParseRequiredString(parts[CommonPayloadParts.SenderUserId], out var senderUserId))
                return false;

            if (!TryParseIntWithMin(parts[PlacementTimeoutPayloadParts.PlayerSlot], 0, out var playerSlot))
                return false;

            if (!int.TryParse(parts[PlacementTimeoutPayloadParts.AutoPlaceSeed], NumberStyles.Integer, CultureInfo.InvariantCulture, out var autoPlaceSeed))
                return false;

            message = new BattleshipPlacementTimeoutMessage(commandId, senderUserId, playerSlot, autoPlaceSeed, ParseInt64OrDefault(parts[PlacementTimeoutPayloadParts.ClientTick]));
            return true;
        }

        public static byte[] SerializeRecovery(BattleshipRecoveryMessage message)
        {
            var line = string.Concat(
                RecoveryPayloadParts.Prefix, "|",
                message.CommandId.ToString("N"), "|",
                EncodeField(message.SenderUserId), "|",
                message.MatchRoundId.ToString(CultureInfo.InvariantCulture), "|",
                message.Phase.ToString(CultureInfo.InvariantCulture), "|",
                message.ActivePlayerSlot.ToString(CultureInfo.InvariantCulture), "|",
                message.PlacementTimerRemainingMs.ToString(CultureInfo.InvariantCulture), "|",
                message.MoveTimerRemainingMs.ToString(CultureInfo.InvariantCulture), "|",
                message.Player0ConsecutiveTimeouts.ToString(CultureInfo.InvariantCulture), "|",
                message.Player1ConsecutiveTimeouts.ToString(CultureInfo.InvariantCulture), "|",
                message.WinnerSlot.ToString(CultureInfo.InvariantCulture), "|",
                message.FinishStatus.ToString(CultureInfo.InvariantCulture), "|",
                message.ClientTick.ToString(CultureInfo.InvariantCulture), "|",
                EncodePayload(message.Player0LayoutPayload), "|",
                EncodePayload(message.Player1LayoutPayload), "|",
                EncodePayload(message.Player0OpponentMarksPayload), "|",
                EncodePayload(message.Player1OpponentMarksPayload));

            return Encoding.UTF8.GetBytes(line);
        }

        public static bool TryDeserializeRecovery(byte[] payload, out BattleshipRecoveryMessage message)
        {
            message = default;

            if (!TryGetPayloadParts(payload, RecoveryPayloadParts.Prefix, RecoveryPayloadParts.Count, out var parts))
                return false;

            if (!TryParseRecoveryHeader(parts, out var commandId, out var senderUserId, out var matchRoundId, out var phase))
                return false;

            ParseRecoveryState(parts, out var activePlayerSlot, out var placementTimerRemainingMs, out var moveTimerRemainingMs, out var player0Timeouts, out var player1Timeouts, out var winnerSlot, out var finishStatus, out var clientTick);

            if (!TryParseRecoveryPayloads(parts, out var player0Layout, out var player1Layout, out var player0Marks, out var player1Marks))
                return false;

            message = CreateRecoveryMessage(commandId, senderUserId, matchRoundId, phase, activePlayerSlot, placementTimerRemainingMs, moveTimerRemainingMs, player0Timeouts, player1Timeouts, winnerSlot, finishStatus, clientTick, player0Layout, player1Layout, player0Marks, player1Marks);
            return true;
        }

        private static bool TryGetPayloadParts(byte[] payload, string prefix, int expectedLength, out string[] parts)
        {
            parts = Array.Empty<string>();

            var line = Encoding.UTF8.GetString(payload);
            
            if (string.IsNullOrWhiteSpace(line))
                return false;

            parts = line.Split('|');
            return parts.Length == expectedLength && parts[0] == prefix;
        }

        private static bool TryParseRequiredGuid(string raw, out Guid commandId) =>
            Guid.TryParse(raw, out commandId) && commandId != Guid.Empty;

        private static bool TryParseRequiredString(string raw, out string value)
        {
            value = string.Empty;
            
            return TryDecodeField(raw, out value)
                   && !string.IsNullOrWhiteSpace(value);
        }

        private static bool TryParseIntWithMin(string raw, int minValue, out int value) =>
            int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
            && value >= minValue;

        private static long ParseInt64OrDefault(string raw) =>
            long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : 0L;

        private static int ParseIntOrDefault(string raw, int defaultValue) =>
            int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : defaultValue;

        private static bool TryParseRecoveryHeader(
            string[] parts,
            out Guid commandId,
            out string senderUserId,
            out int matchRoundId,
            out int phase)
        {
            commandId = Guid.Empty;
            senderUserId = string.Empty;
            matchRoundId = 0;
            phase = 0;
            
            return TryParseRequiredGuid(parts[CommonPayloadParts.CommandId], out commandId)
                   && TryParseRequiredString(parts[CommonPayloadParts.SenderUserId], out senderUserId)
                   && TryParseIntWithMin(parts[RecoveryPayloadParts.MatchRoundId], RecoveryPayloadParts.MinMatchRoundId, out matchRoundId)
                   && int.TryParse(parts[RecoveryPayloadParts.Phase], NumberStyles.Integer, CultureInfo.InvariantCulture, out phase);
        }

        private static void ParseRecoveryState(
            string[] parts,
            out int activePlayerSlot,
            out long placementTimerRemainingMs,
            out long moveTimerRemainingMs,
            out int player0Timeouts,
            out int player1Timeouts,
            out int winnerSlot,
            out int finishStatus,
            out long clientTick)
        {
            activePlayerSlot = ParseIntOrDefault(parts[RecoveryPayloadParts.ActivePlayerSlot], -1);
            placementTimerRemainingMs = ParseInt64OrDefault(parts[RecoveryPayloadParts.PlacementTimerRemainingMs]);
            moveTimerRemainingMs = ParseInt64OrDefault(parts[RecoveryPayloadParts.MoveTimerRemainingMs]);
            player0Timeouts = ParseIntOrDefault(parts[RecoveryPayloadParts.Player0ConsecutiveTimeouts], 0);
            player1Timeouts = ParseIntOrDefault(parts[RecoveryPayloadParts.Player1ConsecutiveTimeouts], 0);
            winnerSlot = ParseIntOrDefault(parts[RecoveryPayloadParts.WinnerSlot], -1);
            finishStatus = ParseIntOrDefault(parts[RecoveryPayloadParts.FinishStatus], 0);
            clientTick = ParseInt64OrDefault(parts[RecoveryPayloadParts.ClientTick]);
        }

        private static bool TryParseRecoveryPayloads(
            string[] parts,
            out string player0Layout,
            out string player1Layout,
            out string player0Marks,
            out string player1Marks)
        {
            player0Layout = string.Empty;
            player1Layout = string.Empty;
            player0Marks = string.Empty;
            player1Marks = string.Empty;
            
            return TryDecodePayload(parts[RecoveryPayloadParts.Player0LayoutPayload], out player0Layout)
                   && TryDecodePayload(parts[RecoveryPayloadParts.Player1LayoutPayload], out player1Layout)
                   && TryDecodePayload(parts[RecoveryPayloadParts.Player0OpponentMarksPayload], out player0Marks)
                   && TryDecodePayload(parts[RecoveryPayloadParts.Player1OpponentMarksPayload], out player1Marks);
        }

        private static BattleshipRecoveryMessage CreateRecoveryMessage(Guid commandId, string senderUserId, int matchRoundId, int phase, int activePlayerSlot, long placementTimerRemainingMs, long moveTimerRemainingMs, int player0Timeouts, int player1Timeouts, int winnerSlot, int finishStatus, long clientTick, string player0Layout, string player1Layout, string player0Marks, string player1Marks) =>
            new(
                commandId,
                senderUserId,
                matchRoundId,
                phase,
                activePlayerSlot,
                placementTimerRemainingMs,
                moveTimerRemainingMs,
                player0Timeouts,
                player1Timeouts,
                winnerSlot,
                finishStatus,
                clientTick,
                player0Layout,
                player1Layout,
                player0Marks,
                player1Marks);

        private static string EncodeField(string? value)
        {
            var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            return _encodedFieldPrefix + Convert.ToBase64String(bytes);
        }

        private static bool TryDecodeField(string raw, out string value)
        {
            value = string.Empty;

            if (string.IsNullOrWhiteSpace(raw))
                return false;

            if (!raw.StartsWith(_encodedFieldPrefix, StringComparison.Ordinal))
            {
                value = raw;
                return true;
            }

            var payload = raw.Substring(_encodedFieldPrefix.Length);
            return TryDecodePayload(payload, out value);
        }

        private static string EncodePayload(string? payload)
        {
            var bytes = Encoding.UTF8.GetBytes(payload ?? string.Empty);
            return Convert.ToBase64String(bytes);
        }

        private static bool TryDecodePayload(string? payload, out string decoded)
        {
            decoded = string.Empty;

            if (payload == null)
                return false;

            try
            {
                var bytes = Convert.FromBase64String(payload);
                decoded = Encoding.UTF8.GetString(bytes);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}