#nullable enable

using System;
using Cysharp.Threading.Tasks;
using Fusion;
using UnityEngine;

namespace Runtime.GameModes.Wizard.Online
{
    internal static class FusionRunnerHelpers
    {
        public static bool TryResolveNetworkTime(NetworkRunner? runner, out double networkTime)
        {
            networkTime = 0;

            if (runner == null || !runner.IsRunning)
                return false;

            try
            {
                var runnerType = runner.GetType();
                
                var timeProperty = runnerType.GetProperty("SimulationTime") ??
                                   runnerType.GetProperty("NetworkTime") ??
                                   runnerType.GetProperty("LocalRenderTime");

                return timeProperty != null && TryConvertToDouble(timeProperty.GetValue(runner), out networkTime);
            }
            catch
            {
                return false;
            }
        }

        public static bool IsConnectedToServer(NetworkRunner runner)
        {
            try
            {
                if (TryGetBooleanProperty(runner, "IsConnectedToServer", out var connected))
                    return connected;

                return TryGetBooleanProperty(runner, "IsInSession", out var inSession) && inSession;
            }
            catch
            {
                return false;
            }
        }

        public static int CountPlayers(NetworkRunner runner)
        {
            var count = 0;

            foreach (var _ in runner.ActivePlayers)
            {
                count++;
            }

            return count;
        }

        public static string? GetRemotePlayerId(NetworkRunner runner)
        {
            foreach (var player in runner.ActivePlayers)
            {
                if (player == runner.LocalPlayer)
                    continue;

                return player.ToString();
            }

            return null;
        }

        public static async UniTask<bool> WaitForRemoteRecipientAsync(NetworkRunner runner, TimeSpan timeout, TimeSpan pollDelay)
        {
            var deadline = Time.realtimeSinceStartupAsDouble + timeout.TotalSeconds;

            while (runner != null && runner.IsRunning)
            {
                if (HasRemoteRecipient(runner))
                    return true;

                if (Time.realtimeSinceStartupAsDouble >= deadline)
                    return false;

                await UniTask.Delay(pollDelay);
            }

            return false;
        }

        private static bool HasRemoteRecipient(NetworkRunner runner)
        {
            foreach (var player in runner.ActivePlayers)
            {
                if (player != runner.LocalPlayer)
                    return true;
            }

            return false;
        }

        private static bool TryGetBooleanProperty(object source, string propertyName, out bool value)
        {
            value = false;

            var property = source.GetType().GetProperty(propertyName);

            if (property?.GetValue(source) is not bool propertyValue)
                return false;

            value = propertyValue;
            return true;
        }

        private static bool TryConvertToDouble(object? raw, out double value)
        {
            value = 0;

            if (raw is double asDouble)
            {
                value = asDouble;
                return true;
            }

            if (raw is float asFloat)
            {
                value = asFloat;
                return true;
            }

            if (raw != null && TryGetDoubleProperty(raw, "AsDouble", out value))
                return true;

            return raw != null && double.TryParse(raw.ToString(), out value);
        }

        private static bool TryGetDoubleProperty(object source, string propertyName, out double value)
        {
            value = 0;

            var property = source.GetType().GetProperty(propertyName);

            if (property?.GetValue(source) is not double propertyValue)
                return false;

            value = propertyValue;
            return true;
        }
    }
}