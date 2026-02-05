using System;
using Runtime.GameModes.Wizard;
using Runtime.Gameplay.Moves;

namespace Runtime.Gameplay
{
    public static class LocalMovesConfigMapper
    {
        public static LocalMovesConfig FromLaunchConfig(GameLaunchConfig launchConfig, FieldRenderSpec fieldSpec)
        {
            if (launchConfig == null)
                throw new ArgumentNullException(nameof(launchConfig));
            if (fieldSpec == null)
                throw new ArgumentNullException(nameof(fieldSpec));

            // MVP: стартовый игрок пока не задаётся в Wizard.
            return new LocalMovesConfig(fieldSpec, PlayerMark.X);
        }
    }
}
