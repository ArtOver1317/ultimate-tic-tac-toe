#nullable enable

using System.Collections.Generic;

namespace Runtime.GameModes.Wizard.Configs
{
    /// <summary>
    /// Catalog of bot difficulties available in the wizard.
    /// </summary>
    public interface IBotDifficultyCatalog
    {
        /// <summary>Ordered list of available difficulties.</summary>
        IReadOnlyList<BotDifficulty> Difficulties { get; }
    }
}