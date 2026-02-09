using System;
using Runtime.GameModes.Wizard;

using Runtime.Gameplay;
namespace Runtime.Games.TicTacToe
{
    public sealed class FieldSpecMapper
    {
        public FieldRenderSpec Map(GameLaunchConfig config, IGameCatalog catalog)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));

            if (!catalog.TryGetStrategy(config.GameId, out _))
                throw new InvalidOperationException($"Unknown game id: '{config.GameId}'.");

            if (config.GameConfig is TicTacToeConfig tttConfig)
            {
                return tttConfig.IsUltimate
                    ? FieldRenderSpec.Ultimate()
                    : FieldRenderSpec.Classic(tttConfig.BoardSize);
            }

            throw new InvalidOperationException(
                $"Unsupported game config type: '{config.GameConfig?.GetType().Name ?? "null"}'.");
        }
    }
}
