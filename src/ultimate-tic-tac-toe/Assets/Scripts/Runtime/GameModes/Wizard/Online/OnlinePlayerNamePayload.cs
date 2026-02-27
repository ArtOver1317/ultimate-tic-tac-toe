#nullable enable

using System;
using System.Text;
using Runtime.Infrastructure.Logging;
using Runtime.PlayerProfile;

namespace Runtime.GameModes.Wizard
{
    internal readonly struct OnlinePlayerNamePayloadData
    {
        public bool IsHost { get; }
        public string? CustomName { get; }

        public OnlinePlayerNamePayloadData(bool isHost, string? customName)
        {
            IsHost = isHost;
            CustomName = customName;
        }
    }

    internal static class OnlinePlayerNamePayload
    {
        private const string TypeMarker = "N";
        private const string VersionMarker = "1";

        public static byte[] Serialize(bool isHost, string? customName)
        {
            if (!string.IsNullOrEmpty(customName) && customName.IndexOf('|') >= 0)
                throw new ArgumentException("Custom name contains invalid protocol separator '|'.", nameof(customName));

            if (!string.IsNullOrEmpty(customName) &&
                PlayerNameValidator.ValidateOnConfirm(customName) != PlayerNameValidationError.None)
            {
                throw new ArgumentException("Custom name does not satisfy player-name validator.", nameof(customName));
            }

            var role = isHost ? "H" : "G";
            var hasCustom = string.IsNullOrEmpty(customName) ? "0" : "1";
            var safeCustomName = hasCustom == "1" ? customName! : string.Empty;
            var line = string.Concat(TypeMarker, "|", VersionMarker, "|", role, "|", hasCustom, "|", safeCustomName);
            return Encoding.UTF8.GetBytes(line);
        }

        public static bool TryDeserialize(byte[] payloadBytes, out OnlinePlayerNamePayloadData payload)
        {
            payload = default;

            var line = Encoding.UTF8.GetString(payloadBytes);
            if (string.IsNullOrWhiteSpace(line))
                return false;

            var parts = line.Split('|');
            if (parts.Length != 5 || !string.Equals(parts[0], TypeMarker, StringComparison.Ordinal))
                return false;

            if (!string.Equals(parts[1], VersionMarker, StringComparison.Ordinal))
            {
                GameLog.Warning($"[OnlinePlayerNamePayload] Unsupported payload version: '{parts[1]}'.");
                return false;
            }

            var isHost = parts[2] switch
            {
                "H" => true,
                "G" => false,
                _ => false,
            };

            if (parts[2] != "H" && parts[2] != "G")
            {
                GameLog.Warning($"[OnlinePlayerNamePayload] Unsupported role marker: '{parts[2]}'.");
                return false;
            }

            if (parts[3] == "0")
            {
                if (!string.IsNullOrEmpty(parts[4]))
                    return false;

                payload = new OnlinePlayerNamePayloadData(isHost, null);
                return true;
            }

            if (parts[3] != "1")
            {
                GameLog.Warning($"[OnlinePlayerNamePayload] Unsupported hasCustom marker: '{parts[3]}'.");
                return false;
            }

            var customName = parts[4];
            if (string.IsNullOrWhiteSpace(customName))
                return false;

            if (PlayerNameValidator.ValidateOnConfirm(customName) != PlayerNameValidationError.None)
                return false;

            payload = new OnlinePlayerNamePayloadData(isHost, customName);
            return true;
        }
    }
}

#nullable restore
