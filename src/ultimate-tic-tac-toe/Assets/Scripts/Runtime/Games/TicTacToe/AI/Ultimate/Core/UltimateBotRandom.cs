#nullable enable

using System;
using System.Security.Cryptography;
using System.Text;

namespace Runtime.Games.TicTacToe.AI.Ultimate.Core
{
    public sealed class XorShift32BotRngSession : IBotRngSession
    {
        private const uint _defaultNonZeroSeed = 2463534242u;
        private const int _firstXorShiftBits = 13;
        private const int _secondXorShiftBits = 17;
        private const int _thirdXorShiftBits = 5;
        private const int _floatPrecisionBits = 24;
        private const uint _floatUnitRange = 1u << _floatPrecisionBits;
        private const uint _floatUnitMask = _floatUnitRange - 1u;

        private uint _state;

        public XorShift32BotRngSession(uint seed) => _state = seed == 0 ? _defaultNonZeroSeed : seed;

        public uint NextUInt()
        {
            var value = _state;
            value ^= value << _firstXorShiftBits;
            value ^= value >> _secondXorShiftBits;
            value ^= value << _thirdXorShiftBits;
            _state = value;
            return value;
        }

        public float NextFloat01() => (NextUInt() & _floatUnitMask) / (float)_floatUnitRange;

        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (minInclusive >= maxExclusive) 
                throw new ArgumentOutOfRangeException(nameof(maxExclusive));

            var range = (uint)(maxExclusive - minInclusive);
            var value = NextUInt() % range;
            return minInclusive + (int)value;
        }
    }

    public sealed class BotRngSessionFactory : IBotRngSessionFactory
    {
        private const char _seedMaterialSeparator = '|';
        private const int _seedHashOffsetBytes = 0;

        public IBotRngSession Create(string matchInstanceId, int botSlot, UltimateBotDifficultyProfileData profile)
        {
            if (string.IsNullOrWhiteSpace(matchInstanceId)) 
                throw new ArgumentException("matchInstanceId cannot be empty", nameof(matchInstanceId));

            var seedMaterial = BuildSeedMaterial(matchInstanceId, botSlot, profile);
            var seed = ComputeSeed(seedMaterial);
            return new XorShift32BotRngSession(seed);
        }

        private static string BuildSeedMaterial(string matchInstanceId, int botSlot, UltimateBotDifficultyProfileData profile) =>
            profile.UseSeed
                ? $"{matchInstanceId}{_seedMaterialSeparator}{botSlot}{_seedMaterialSeparator}{profile.Seed}{_seedMaterialSeparator}{profile.ProfileHash}"
                : $"{matchInstanceId}{_seedMaterialSeparator}{botSlot}{_seedMaterialSeparator}{profile.ProfileHash}";

        private static uint ComputeSeed(string seedMaterial)
        {
            var bytes = Encoding.UTF8.GetBytes(seedMaterial);
            byte[] seedHash;
            
            using (var sha = SHA256.Create())
            {
                seedHash = sha.ComputeHash(bytes);
            }
            
            return BitConverter.ToUInt32(seedHash, _seedHashOffsetBytes);
        }
    }
}