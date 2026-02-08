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
            
            return fieldSpec == null
                ? throw new ArgumentNullException(nameof(fieldSpec)) 
                :
                // MVP: стартовый игрок пока не задаётся в Wizard.
                new LocalMovesConfig(fieldSpec, PlayerMark.X);
        }
    }
}
