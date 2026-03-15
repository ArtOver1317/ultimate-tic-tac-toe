#nullable enable

using System;

namespace Runtime.Games.TicTacToe.AI.Core
{
    /// <summary>
    /// Bot-local deterministic RNG (ADR-3).
    /// Based on <see cref="System.Random"/> with explicit seed.
    /// One instance per <see cref="IBotTurnDriver"/>; never shared between bots.
    /// </summary>
    public sealed class BotRandom : IBotRandom
    {
        private readonly Random _random;

        public BotRandom(int seed) => _random = new Random(seed);

        public float NextFloat01() => (float)_random.NextDouble();

        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (minInclusive >= maxExclusive)
            {
                throw new ArgumentOutOfRangeException(nameof(maxExclusive),
                    $"maxExclusive ({maxExclusive}) must be greater than minInclusive ({minInclusive}).");
            }

            return _random.Next(minInclusive, maxExclusive);
        }
    }
}