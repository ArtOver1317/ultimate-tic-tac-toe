using System.Collections.Generic;

#nullable enable

namespace Runtime.GameModes.Wizard
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