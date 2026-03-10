#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Runtime.GameModes.Wizard.Matchmaking.Contracts;

namespace Runtime.GameModes.Wizard.Matchmaking.Services
{
    public static class MatchmakingParamsHasher
    {
        private const string _base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        private const int _hashPrefixLength = 8;

        public static string Compute(MatchmakingRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            var normalizedGameId = NormalizeGameId(request.GameId);
            var parameters = request.GameConfig.GetMatchmakingParams() ?? Array.Empty<KeyValuePair<string, string>>();
            var orderedParameters = new List<KeyValuePair<string, string>>(parameters);
            orderedParameters.Sort(CompareParams);

            var canonical = BuildCanonicalString(normalizedGameId, request.MoveTimeLimitSeconds, orderedParameters);
            var bytes = Encoding.UTF8.GetBytes(canonical);
            byte[] hashBytes;
            
            using (var sha256 = SHA256.Create())
            {
                hashBytes = sha256.ComputeHash(bytes);
            }

            return ToBase32Prefix(hashBytes, _hashPrefixLength);
        }

        public static string NormalizeGameId(string gameId) =>
            gameId == null 
                ? throw new ArgumentNullException(nameof(gameId)) 
                : gameId.Trim().ToLowerInvariant();

        private static int CompareParams(KeyValuePair<string, string> left, KeyValuePair<string, string> right)
        {
            var keyComparison = string.Compare(left.Key, right.Key, StringComparison.Ordinal);
            return keyComparison != 0 ? keyComparison : string.Compare(left.Value, right.Value, StringComparison.Ordinal);
        }

        private static string BuildCanonicalString(
            string normalizedGameId,
            int moveTimeLimitSeconds,
            IReadOnlyList<KeyValuePair<string, string>> parameters)
        {
            var builder = new StringBuilder();
            builder.Append(normalizedGameId);
            builder.Append('|');
            builder.Append(moveTimeLimitSeconds.ToString(CultureInfo.InvariantCulture));

            for (var i = 0; i < parameters.Count; i++)
            {
                var parameter = parameters[i];

                if (parameter.Key == null)
                    throw new InvalidOperationException("Matchmaking parameter key cannot be null.");

                if (parameter.Value == null)
                    throw new InvalidOperationException("Matchmaking parameter value cannot be null.");

                builder.Append('|');
                builder.Append(parameter.Key);
                builder.Append('=');
                builder.Append(parameter.Value);
            }

            return builder.ToString();
        }

        private static string ToBase32Prefix(ReadOnlySpan<byte> bytes, int maxChars)
        {
            if (bytes.IsEmpty || maxChars <= 0)
                return string.Empty;

            var output = new StringBuilder(maxChars);
            var bitBuffer = 0;
            var bitCount = 0;

            for (var i = 0; i < bytes.Length; i++)
            {
                bitBuffer = (bitBuffer << 8) | bytes[i];
                bitCount += 8;

                while (bitCount >= 5 && output.Length < maxChars)
                {
                    var index = (bitBuffer >> (bitCount - 5)) & 31;
                    output.Append(_base32Alphabet[index]);
                    bitCount -= 5;
                }

                if (output.Length >= maxChars)
                    break;
            }

            if (bitCount > 0 && output.Length < maxChars)
            {
                var index = (bitBuffer << (5 - bitCount)) & 31;
                output.Append(_base32Alphabet[index]);
            }

            return output.ToString();
        }
    }
}
