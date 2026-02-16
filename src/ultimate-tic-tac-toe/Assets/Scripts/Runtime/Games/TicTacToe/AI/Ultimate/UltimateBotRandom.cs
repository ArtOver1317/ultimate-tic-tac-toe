#nullable enable

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace Runtime.Games.TicTacToe.AI.Ultimate
{
    public sealed class XorShift32BotRngSession : IBotRngSession
    {
        private uint _state;

        public XorShift32BotRngSession(uint seed)
        {
            _state = seed == 0 ? 2463534242u : seed;
        }

        public uint NextUInt()
        {
            var x = _state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _state = x;
            return x;
        }

        public float NextFloat01()
        {
            return (NextUInt() & 0x00FFFFFFu) / 16777216f;
        }

        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (minInclusive >= maxExclusive)
            {
                throw new ArgumentOutOfRangeException(nameof(maxExclusive));
            }

            var range = (uint)(maxExclusive - minInclusive);
            var value = NextUInt() % range;
            return minInclusive + (int)value;
        }
    }

    public sealed class BotRandomizer : IBotRandomizer
    {
        public void Shuffle<T>(IList<T> values, IBotRngSession rng)
        {
            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            if (rng == null)
            {
                throw new ArgumentNullException(nameof(rng));
            }

            for (var i = values.Count - 1; i > 0; i--)
            {
                var j = rng.NextInt(0, i + 1);
                (values[i], values[j]) = (values[j], values[i]);
            }
        }

        public int WeightedChoiceIndex(IReadOnlyList<float> weights, IBotRngSession rng)
        {
            if (weights == null)
            {
                throw new ArgumentNullException(nameof(weights));
            }

            if (rng == null)
            {
                throw new ArgumentNullException(nameof(rng));
            }

            if (weights.Count == 0)
            {
                throw new ArgumentException("weights must not be empty", nameof(weights));
            }

            var total = 0f;
            for (var i = 0; i < weights.Count; i++)
            {
                total += Math.Max(0f, weights[i]);
            }

            if (total <= 0f)
            {
                return 0;
            }

            var threshold = rng.NextFloat01() * total;
            var cumulative = 0f;
            for (var i = 0; i < weights.Count; i++)
            {
                cumulative += Math.Max(0f, weights[i]);
                if (threshold <= cumulative)
                {
                    return i;
                }
            }

            return weights.Count - 1;
        }
    }

    public sealed class BotRngSessionFactory : IBotRngSessionFactory
    {
        public IBotRngSession Create(string matchInstanceId, int botSlot, UltimateBotDifficultyProfileData profile)
        {
            if (string.IsNullOrWhiteSpace(matchInstanceId))
            {
                throw new ArgumentException("matchInstanceId cannot be empty", nameof(matchInstanceId));
            }

            var seedMaterial = profile.UseSeed
                ? $"{matchInstanceId}|{botSlot}|{profile.Seed}|{profile.ProfileHash}"
                : $"{matchInstanceId}|{botSlot}|{profile.ProfileHash}";

            var bytes = Encoding.UTF8.GetBytes(seedMaterial);
            byte[] hash;
            using (var sha = SHA256.Create())
            {
                hash = sha.ComputeHash(bytes);
            }
            var seed = BitConverter.ToUInt32(hash, 0);

            return new XorShift32BotRngSession(seed);
        }
    }
}
