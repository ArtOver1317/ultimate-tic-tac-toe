#nullable enable

using System;
using System.Diagnostics;
using UnityEngine;

namespace Runtime.GameModes.Wizard.Online
{
    public static class OnlineIdentityProvider
    {
        public static string ResolveCurrentUserId()
        {
            var baseId = SystemInfo.deviceUniqueIdentifier;
            
            if (string.IsNullOrWhiteSpace(baseId))
                baseId = Guid.NewGuid().ToString("N");

            try
            {
                var processId = Process.GetCurrentProcess().Id;
                return $"{baseId}-{processId}";
            }
            catch
            {
                return baseId;
            }
        }

        public static string ResolveDefaultRegion() => "eu";
    }
}