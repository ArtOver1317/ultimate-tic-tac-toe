#nullable enable

using System;
using System.Collections.Generic;

namespace Runtime.GameModes.Wizard.Configs
{
    /// <summary>
    /// Default bot difficulty catalog.
    /// </summary>
    public sealed class BotDifficultyCatalog : IBotDifficultyCatalog
    {
        private static readonly IReadOnlyList<BotDifficulty> _defaultDifficulties = Array.AsReadOnly(new[]
        {
            new BotDifficulty("Easy", "GameWizard.MatchSetup.BotDifficulty.Easy", 0),
            new BotDifficulty("Normal", "GameWizard.MatchSetup.BotDifficulty.Normal", 1),
            new BotDifficulty("Hard", "GameWizard.MatchSetup.BotDifficulty.Hard", 2),
        });

        public IReadOnlyList<BotDifficulty> Difficulties => _defaultDifficulties;
    }
}