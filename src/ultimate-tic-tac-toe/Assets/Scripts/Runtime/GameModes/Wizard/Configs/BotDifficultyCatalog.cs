#nullable enable

using System;
using System.Collections.Generic;

namespace Runtime.GameModes.Wizard
{
    /// <summary>
    /// Default bot difficulty catalog.
    /// </summary>
    public sealed class BotDifficultyCatalog : IBotDifficultyCatalog
    {
        private static readonly IReadOnlyList<BotDifficulty> _defaultDifficulties = Array.AsReadOnly(new[]
        {
            new BotDifficulty("Easy", "GameModeWizard.MatchSetup.BotDifficulty.Easy", 0),
            new BotDifficulty("Normal", "GameModeWizard.MatchSetup.BotDifficulty.Normal", 1),
            new BotDifficulty("Hard", "GameModeWizard.MatchSetup.BotDifficulty.Hard", 2),
        });

        public IReadOnlyList<BotDifficulty> Difficulties => _defaultDifficulties;
    }
}