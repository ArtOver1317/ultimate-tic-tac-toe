#nullable enable

using System;
using System.Globalization;
using System.Text;

namespace Runtime.GameModes.Wizard
{
    internal static class OnlinePayloadSerialization
    {
        public static byte[] SerializeMatchConfig(OnlineMatchConfigPayload payload)
        {
            var line = string.Concat(
                "C|",
                payload.GameId.Replace("|", string.Empty), "|",
                payload.BoardSize.ToString(CultureInfo.InvariantCulture), "|",
                payload.IsUltimate ? "1" : "0");

            return Encoding.UTF8.GetBytes(line);
        }

        public static bool TryDeserializeMatchConfig(byte[] payloadBytes, out OnlineMatchConfigPayload payload)
        {
            payload = default;

            var line = Encoding.UTF8.GetString(payloadBytes);
            if (string.IsNullOrWhiteSpace(line))
                return false;

            var parts = line.Split('|');
            if (parts.Length != 4 || parts[0] != "C")
                return false;

            var gameId = parts[1];
            if (string.IsNullOrWhiteSpace(gameId))
                return false;

            if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var boardSize) || boardSize <= 0)
                return false;

            var isUltimate = parts[3] == "1";

            payload = new OnlineMatchConfigPayload(gameId, boardSize, isUltimate);
            return true;
        }

        public static byte[] SerializeCountdownTarget(double targetNetworkTimeSeconds)
        {
            var line = string.Concat("T|", targetNetworkTimeSeconds.ToString("R", CultureInfo.InvariantCulture));
            return Encoding.UTF8.GetBytes(line);
        }

        public static bool TryDeserializeCountdownTarget(byte[] payloadBytes, out double targetNetworkTimeSeconds)
        {
            targetNetworkTimeSeconds = 0d;

            var line = Encoding.UTF8.GetString(payloadBytes);
            if (string.IsNullOrWhiteSpace(line))
                return false;

            var parts = line.Split('|');
            if (parts.Length != 2 || parts[0] != "T")
                return false;

            return double.TryParse(parts[1], NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out targetNetworkTimeSeconds);
        }
    }
}

#nullable restore
