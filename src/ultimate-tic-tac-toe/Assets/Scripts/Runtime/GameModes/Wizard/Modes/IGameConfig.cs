using System.Collections.Generic;

namespace Runtime.GameModes.Wizard.Modes
{
    /// <summary>
    /// Mode-specific configuration used by Wizard and matchmaking.
    /// Returned collection must contain deterministic key-value pairs in sorted key order.
    /// All values needed for matchmaking must have explicit defaults (no implicit omitted fields).
    /// </summary>
    public interface IGameConfig
    {
        IReadOnlyList<KeyValuePair<string, string>> GetMatchmakingParams();
    }
}
