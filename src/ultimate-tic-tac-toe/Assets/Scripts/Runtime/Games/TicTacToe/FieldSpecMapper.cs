using System;
using Runtime.GameModes.Wizard;
using Runtime.Games.Battleship;

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

            if (string.Equals(config.GameId, UltimateTicTacToeStrategy.DefaultGameId, StringComparison.Ordinal))
            {
                if (config.GameConfig is UltimateTicTacToeConfig)
                    return FieldRenderSpec.Ultimate();

                throw new InvalidOperationException(
                    $"Unsupported game config type: '{config.GameConfig?.GetType().Name ?? "null"}'.");
            }

            if (string.Equals(config.GameId, TicTacToeStrategy.DefaultGameId, StringComparison.Ordinal)
                && config.GameConfig is TicTacToeConfig tttConfig)
                return FieldRenderSpec.Classic(tttConfig.BoardSize);

            if (string.Equals(config.GameId, BattleshipStrategy.DefaultGameId, StringComparison.Ordinal)
                && config.GameConfig is BattleshipConfig)
                return FieldRenderSpec.Classic(10);

            throw new InvalidOperationException(
                $"Unsupported game config type: '{config.GameConfig?.GetType().Name ?? "null"}'.");
        }
    }
}
