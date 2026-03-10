#nullable enable

using System.Collections.Generic;
using System.Globalization;

namespace Runtime.GameModes.Wizard.Modes
{
    public sealed class BattleshipConfig : IGameConfig
    {
        public int PlacementTimeLimitSeconds { get; }

        public BattleshipConfig(int placementTimeLimitSeconds) =>
            PlacementTimeLimitSeconds = placementTimeLimitSeconds < 0
                ? 0
                : placementTimeLimitSeconds;

        public IReadOnlyList<KeyValuePair<string, string>> GetMatchmakingParams() =>
            new[]
            {
                new KeyValuePair<string, string>("placementTimeLimitSeconds", PlacementTimeLimitSeconds.ToString(CultureInfo.InvariantCulture)),
            };
    }
}
