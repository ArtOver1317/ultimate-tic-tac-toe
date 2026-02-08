using System;
using Runtime.GameModes.Wizard;

namespace Runtime.Gameplay
{
    public sealed class FieldSpecMapper
    {
        public FieldRenderSpec Map(GameLaunchConfig config, IGameModeCatalog catalog)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));
            
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));

            if (!catalog.TryGetStrategy(config.GameModeId, out var strategy) || strategy == null)
                throw new InvalidOperationException($"Unknown game mode id: '{config.GameModeId}'.");

            return strategy.Metadata.FieldKind switch
            {
                FieldKind.Classic => config.ModeConfig is ClassicModeConfig classicConfig
                    ? FieldRenderSpec.Classic(classicConfig.BoardSize)
                    : throw new InvalidOperationException("Classic mode config is missing or invalid."),

                FieldKind.Ultimate => config.ModeConfig is UltimateModeConfig
                    ? FieldRenderSpec.Ultimate()
                    : throw new InvalidOperationException("Ultimate mode config is missing or invalid."),

                _ => throw new InvalidOperationException($"Unsupported field kind: '{strategy.Metadata.FieldKind}'."),
            };
        }
    }
}
