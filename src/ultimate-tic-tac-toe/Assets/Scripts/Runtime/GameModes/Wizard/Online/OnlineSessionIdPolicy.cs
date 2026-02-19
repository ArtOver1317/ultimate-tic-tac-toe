#nullable enable

using System;
using System.Security.Cryptography;
using System.Text;
using Cysharp.Threading.Tasks;

namespace Runtime.GameModes.Wizard
{
    public static class OnlineSessionIdFormatter
    {
        public const int CanonicalLength = 6;
        private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

        public static string Normalize(string rawInput)
        {
            if (string.IsNullOrWhiteSpace(rawInput))
                return string.Empty;

            var upper = rawInput.Trim().ToUpperInvariant();
            var builder = new StringBuilder(upper.Length);

            for (var i = 0; i < upper.Length; i++)
            {
                var ch = upper[i];

                if (ch == '-' || ch == ' ' || ch == '_')
                    continue;

                builder.Append(ch);
            }

            return builder.ToString();
        }

        public static bool TryNormalizeToCanonical(string rawInput, out string canonical)
        {
            canonical = Normalize(rawInput);

            if (canonical.Length != CanonicalLength)
                return false;

            for (var i = 0; i < canonical.Length; i++)
            {
                if (Alphabet.IndexOf(canonical[i], StringComparison.Ordinal) < 0)
                    return false;
            }

            return true;
        }
    }

    public sealed class OnlineSessionIdLifecycle
    {
        private readonly Func<string> _candidateFactory;

        public string CandidateSessionId { get; private set; } = string.Empty;
        public string? ActiveSessionId { get; private set; }

        public OnlineSessionIdLifecycle(Func<string>? candidateFactory = null)
        {
            _candidateFactory = candidateFactory ?? GenerateCandidate;
        }

        public void EnterHumanSetup()
        {
            if (!string.IsNullOrWhiteSpace(CandidateSessionId))
                return;

            CandidateSessionId = CreateValidatedCandidate();
        }

        public void ActivateCandidateAfterHostStart()
        {
            if (string.IsNullOrWhiteSpace(CandidateSessionId))
                CandidateSessionId = CreateValidatedCandidate();

            ActiveSessionId = CandidateSessionId;
        }

        public void SetCandidateFromInput(string rawSessionIdInput)
        {
            if (!OnlineSessionIdFormatter.TryNormalizeToCanonical(rawSessionIdInput, out var canonical))
                throw new ArgumentException("SessionId must be a valid canonical invite code.", nameof(rawSessionIdInput));

            CandidateSessionId = canonical;
        }

        public void InvalidateActiveSession() => ActiveSessionId = null;

        public void RegenerateCandidateForIdle()
        {
            CandidateSessionId = CreateValidatedCandidate();
        }

        public void ResetToIdleAfterCancelledOrFailedFlow()
        {
            InvalidateActiveSession();
            RegenerateCandidateForIdle();
        }

        private string CreateValidatedCandidate()
        {
            var raw = _candidateFactory();

            if (!OnlineSessionIdFormatter.TryNormalizeToCanonical(raw, out var canonical))
                throw new InvalidOperationException("Candidate factory returned invalid session id.");

            return canonical;
        }

        private static string GenerateCandidate()
        {
            Span<byte> bytes = stackalloc byte[OnlineSessionIdFormatter.CanonicalLength];
            RandomNumberGenerator.Fill(bytes);

            Span<char> chars = stackalloc char[OnlineSessionIdFormatter.CanonicalLength];

            for (var i = 0; i < bytes.Length; i++)
                chars[i] = AlphabetAt(bytes[i] % 32);

            return new string(chars);
        }

        private static char AlphabetAt(int index)
        {
            const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
            return alphabet[index];
        }
    }

    public static class OnlineJoinBySessionId
    {
        public static async UniTask<GatewayOperationResult> ExecuteAsync(
            string rawSessionIdInput,
            string region,
            string currentUserId,
            IPhotonSessionGateway gateway)
        {
            if (gateway == null)
                throw new ArgumentNullException(nameof(gateway));

            if (!OnlineSessionIdFormatter.TryNormalizeToCanonical(rawSessionIdInput, out var canonical))
                return GatewayOperationResult.Failed(OnlineErrorCode.InvalidSessionIdFormat);

            var sessionId = new SessionId(canonical);
            return await gateway.JoinSessionAsync(sessionId, region, currentUserId);
        }
    }
}

#nullable restore