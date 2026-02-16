#nullable enable

using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Runtime.Games.TicTacToe.AI.Ultimate
{
    [CreateAssetMenu(fileName = "UltimateBotProfile", menuName = "TicTacToe/AI/Ultimate/Bot Profile")]
    public sealed class UltimateBotProfile : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private string ProfileId = "easy";
        [SerializeField] private string ProfileVersion = "1.0.0";

        [Header("Search")]
        [SerializeField, Min(16)] private int TimeBudgetMs = 250;
        [SerializeField, Min(1)] private int MinSearchDepth = 1;
        [SerializeField, Min(1)] private int MaxSearchDepth = 3;
        [SerializeField, Min(64)] private int MaxEvaluatedNodes = 10_000;
        [SerializeField, Min(1)] private int TopCandidateCount = 3;
        [SerializeField, Range(0f, 1f)] private float Noise = 0.15f;

        [Header("Hard Rules Probabilities")]
        [SerializeField, Range(0f, 1f)] private float MustWinGlobalNowProbability = 1f;
        [SerializeField, Range(0f, 1f)] private float MustBlockGlobalNowProbability = 1f;
        [SerializeField, Range(0f, 1f)] private float MustWinLocalNowProbability = 1f;
        [SerializeField, Range(0f, 1f)] private float MustBlockLocalNowProbability = 1f;

        [Header("Determinism")]
        [SerializeField] private bool UseSeed;
        [SerializeField] private int Seed;

        [Header("UX")]
        [SerializeField, Min(0)] private int PreMoveDelayMs = 120;

        [Header("Diagnostics")]
        [SerializeField] private bool EnableDiagnostics;

        [Header("Evaluation Weights")]
        [SerializeField, Min(0f)] private float GlobalControlWeight = 1f;
        [SerializeField, Min(0f)] private float GlobalThreatWeight = 1f;
        [SerializeField, Min(0f)] private float LocalThreatWeight = 1f;
        [SerializeField, Min(0f)] private float SteeringWeight = 0.75f;
        [SerializeField, Min(0f)] private float FlexibilityWeight = 0.5f;

        public string Id => string.IsNullOrWhiteSpace(ProfileId) ? "unknown" : ProfileId.Trim();

        public UltimateBotDifficultyProfileData ToValidatedData()
        {
            var normalizedId = NormalizeOrDefault(ProfileId, "unknown", nameof(ProfileId));
            var normalizedVersion = NormalizeOrDefault(ProfileVersion, "1.0.0", nameof(ProfileVersion));

            var timeBudget = ClampWarn(TimeBudgetMs, 16, 30_000, nameof(TimeBudgetMs));
            var minDepth = ClampWarn(MinSearchDepth, 1, 30, nameof(MinSearchDepth));
            var maxDepth = ClampWarn(MaxSearchDepth, minDepth, 30, nameof(MaxSearchDepth));
            var maxNodes = ClampWarn(MaxEvaluatedNodes, 64, 5_000_000, nameof(MaxEvaluatedNodes));
            var topCandidates = ClampWarn(TopCandidateCount, 1, 81, nameof(TopCandidateCount));
            var noise = ClampWarn(Noise, 0f, 1f, nameof(Noise));

            var mustWinGlobal = ClampWarn(MustWinGlobalNowProbability, 0f, 1f, nameof(MustWinGlobalNowProbability));
            var mustBlockGlobal = ClampWarn(MustBlockGlobalNowProbability, 0f, 1f, nameof(MustBlockGlobalNowProbability));
            var mustWinLocal = ClampWarn(MustWinLocalNowProbability, 0f, 1f, nameof(MustWinLocalNowProbability));
            var mustBlockLocal = ClampWarn(MustBlockLocalNowProbability, 0f, 1f, nameof(MustBlockLocalNowProbability));

            var preMoveDelay = ClampWarn(PreMoveDelayMs, 0, 30_000, nameof(PreMoveDelayMs));

            var weights = new EvaluationWeights(
                Mathf.Max(0f, GlobalControlWeight),
                Mathf.Max(0f, GlobalThreatWeight),
                Mathf.Max(0f, LocalThreatWeight),
                Mathf.Max(0f, SteeringWeight),
                Mathf.Max(0f, FlexibilityWeight));

            var canonicalJson = BuildCanonicalJson(
                normalizedId,
                normalizedVersion,
                timeBudget,
                minDepth,
                maxDepth,
                maxNodes,
                topCandidates,
                noise,
                mustWinGlobal,
                mustBlockGlobal,
                mustWinLocal,
                mustBlockLocal,
                UseSeed,
                Seed,
                preMoveDelay,
                EnableDiagnostics,
                weights);

            var profileHash = ComputeSha256LowerHex(canonicalJson);

            return new UltimateBotDifficultyProfileData(
                normalizedId,
                normalizedVersion,
                profileHash,
                timeBudget,
                minDepth,
                maxDepth,
                maxNodes,
                topCandidates,
                noise,
                mustWinGlobal,
                mustBlockGlobal,
                mustWinLocal,
                mustBlockLocal,
                UseSeed,
                Seed,
                preMoveDelay,
                EnableDiagnostics,
                weights);
        }

        private static string NormalizeOrDefault(string? value, string fallback, string fieldName)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }

            Debug.LogWarning($"[UltimateBotProfile] {fieldName} is empty, fallback='{fallback}'.");
            return fallback;
        }

        private static int ClampWarn(int value, int min, int max, string fieldName)
        {
            if (value >= min && value <= max)
            {
                return value;
            }

            var clamped = Mathf.Clamp(value, min, max);
            Debug.LogWarning($"[UltimateBotProfile] {fieldName}={value} out of range [{min}..{max}], clamped to {clamped}.");
            return clamped;
        }

        private static float ClampWarn(float value, float min, float max, string fieldName)
        {
            if (value >= min && value <= max)
            {
                return value;
            }

            var clamped = Mathf.Clamp(value, min, max);
            Debug.LogWarning($"[UltimateBotProfile] {fieldName}={value.ToString(CultureInfo.InvariantCulture)} out of range [{min.ToString(CultureInfo.InvariantCulture)}..{max.ToString(CultureInfo.InvariantCulture)}], clamped to {clamped.ToString(CultureInfo.InvariantCulture)}.");
            return clamped;
        }

        private static string BuildCanonicalJson(
            string profileId,
            string profileVersion,
            int timeBudgetMs,
            int minSearchDepth,
            int maxSearchDepth,
            int maxEvaluatedNodes,
            int topCandidateCount,
            float noise,
            float mustWinGlobalNowProbability,
            float mustBlockGlobalNowProbability,
            float mustWinLocalNowProbability,
            float mustBlockLocalNowProbability,
            bool useSeed,
            int seed,
            int preMoveDelayMs,
            bool enableDiagnostics,
            EvaluationWeights weights)
        {
            var ci = CultureInfo.InvariantCulture;
            var sb = new StringBuilder(512);
            sb.Append('{');
            AppendBoolProperty(sb, "enableDiagnostics", enableDiagnostics);
            AppendSeparator(sb);
            AppendNumberProperty(sb, "maxEvaluatedNodes", maxEvaluatedNodes.ToString(ci));
            AppendSeparator(sb);
            AppendNumberProperty(sb, "maxSearchDepth", maxSearchDepth.ToString(ci));
            AppendSeparator(sb);
            AppendNumberProperty(sb, "minSearchDepth", minSearchDepth.ToString(ci));
            AppendSeparator(sb);
            AppendNumberProperty(sb, "mustBlockGlobalNowProbability", mustBlockGlobalNowProbability.ToString("R", ci));
            AppendSeparator(sb);
            AppendNumberProperty(sb, "mustBlockLocalNowProbability", mustBlockLocalNowProbability.ToString("R", ci));
            AppendSeparator(sb);
            AppendNumberProperty(sb, "mustWinGlobalNowProbability", mustWinGlobalNowProbability.ToString("R", ci));
            AppendSeparator(sb);
            AppendNumberProperty(sb, "mustWinLocalNowProbability", mustWinLocalNowProbability.ToString("R", ci));
            AppendSeparator(sb);
            AppendNumberProperty(sb, "noise", noise.ToString("R", ci));
            AppendSeparator(sb);
            AppendNumberProperty(sb, "preMoveDelayMs", preMoveDelayMs.ToString(ci));
            AppendSeparator(sb);
            AppendStringProperty(sb, "profileId", profileId);
            AppendSeparator(sb);
            AppendStringProperty(sb, "profileVersion", profileVersion);
            AppendSeparator(sb);
            AppendNumberProperty(sb, "seed", seed.ToString(ci));
            AppendSeparator(sb);
            AppendNumberProperty(sb, "timeBudgetMs", timeBudgetMs.ToString(ci));
            AppendSeparator(sb);
            AppendNumberProperty(sb, "topCandidateCount", topCandidateCount.ToString(ci));
            AppendSeparator(sb);
            AppendBoolProperty(sb, "useSeed", useSeed);
            AppendSeparator(sb);
            sb.Append("\"weights\":{");
            AppendNumberProperty(sb, "flexibilityWeight", weights.FlexibilityWeight.ToString("R", ci));
            AppendSeparator(sb);
            AppendNumberProperty(sb, "globalControlWeight", weights.GlobalControlWeight.ToString("R", ci));
            AppendSeparator(sb);
            AppendNumberProperty(sb, "globalThreatWeight", weights.GlobalThreatWeight.ToString("R", ci));
            AppendSeparator(sb);
            AppendNumberProperty(sb, "localThreatWeight", weights.LocalThreatWeight.ToString("R", ci));
            AppendSeparator(sb);
            AppendNumberProperty(sb, "steeringWeight", weights.SteeringWeight.ToString("R", ci));
            sb.Append('}');
            sb.Append('}');
            return sb.ToString();
        }

        private static void AppendSeparator(StringBuilder sb) => sb.Append(',');

        private static void AppendStringProperty(StringBuilder sb, string key, string value)
        {
            sb.Append('"').Append(key).Append("\":");
            sb.Append('"').Append(EscapeJson(value)).Append('"');
        }

        private static void AppendNumberProperty(StringBuilder sb, string key, string value)
            => sb.Append('"').Append(key).Append("\":").Append(value);

        private static void AppendBoolProperty(StringBuilder sb, string key, bool value)
            => sb.Append('"').Append(key).Append("\":").Append(value ? "true" : "false");

        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var escaped = new StringBuilder(value.Length + 8);
            for (var i = 0; i < value.Length; i++)
            {
                var ch = value[i];
                switch (ch)
                {
                    case '\\':
                        escaped.Append("\\\\");
                        break;
                    case '"':
                        escaped.Append("\\\"");
                        break;
                    case '\n':
                        escaped.Append("\\n");
                        break;
                    case '\r':
                        escaped.Append("\\r");
                        break;
                    case '\t':
                        escaped.Append("\\t");
                        break;
                    default:
                        escaped.Append(ch);
                        break;
                }
            }

            return escaped.ToString();
        }

        private static string ComputeSha256LowerHex(string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            byte[] hashBytes;
            using (var sha = SHA256.Create())
            {
                hashBytes = sha.ComputeHash(bytes);
            }

            var sb = new StringBuilder(hashBytes.Length * 2);
            for (var i = 0; i < hashBytes.Length; i++)
            {
                sb.Append(hashBytes[i].ToString("x2", CultureInfo.InvariantCulture));
            }

            return sb.ToString();
        }
    }
}
