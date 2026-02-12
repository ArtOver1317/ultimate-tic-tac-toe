#nullable enable

using System;
using UnityEngine;

namespace Runtime.Games.TicTacToe.AI
{
    /// <summary>
    /// Authoring ScriptableObject for bot difficulty profile (ADR-9).
    /// Game designers edit this in Inspector.
    /// <see cref="ToValidatedData"/> produces a sanitized <see cref="BotProfileData"/> value type
    /// for the engine with clamping and warnings.
    /// </summary>
    [CreateAssetMenu(fileName = "BotProfile", menuName = "TicTacToe/AI/Bot Profile")]
    public sealed class BotProfile : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private string DifficultyId = "easy";

        [Header("Hard Rules")]
        [SerializeField, Range(0f, 1f)] private float MustWinNowProbability = 0.9f;
        [SerializeField, Range(0f, 1f)] private float MustBlockNowProbability = 0.75f;

        [Header("Budget / Search")]
        [SerializeField, Min(50)] private int TimeBudgetMs = 200;
        [SerializeField, Min(1)] private int MinSearchDepth = 1;
        [SerializeField, Min(1)] private int MaxSearchDepth = 3;

        [Header("Candidate Selection")]
        [SerializeField, Min(1)] private int TopCandidateCount = 5;
        [SerializeField, Range(0f, 1f)] private float Noise = 0.6f;
        [SerializeField, Range(-1f, 1f)] private float RiskBias;

        [Header("Evaluation Weights")]
        [SerializeField, Min(0f)] private float AttackWeight = 1f;
        [SerializeField, Min(0f)] private float DefenseWeight = 1f;
        [SerializeField, Min(0f)] private float CenterWeight = 0.5f;
        [SerializeField, Min(0f)] private float IntersectionWeight = 0.5f;

        [Header("Determinism")]
        [SerializeField] private bool UseSeed;
        [SerializeField] private int Seed;

        [Header("UX")]
        [SerializeField, Min(0)] private int PreMoveDelayMs = 150;

        [Header("Debug")]
        [SerializeField] private bool EnableDiagnostics;

        // ── Public read-only accessors ──
        public string Id => DifficultyId;
        public bool UseFixedSeed => UseSeed;
        public int FixedSeed => Seed;
        public int PreMoveDelay => PreMoveDelayMs;

        /// <summary>
        /// Produces a validated, clamped data snapshot for the engine.
        /// Logs warnings for out-of-range values but never throws.
        /// </summary>
        public BotProfileData ToValidatedData()
        {
            var winP = ClampWarn(MustWinNowProbability, 0f, 1f, nameof(MustWinNowProbability));
            var blockP = ClampWarn(MustBlockNowProbability, 0f, 1f, nameof(MustBlockNowProbability));
            var budget = ClampWarn(TimeBudgetMs, 50, 30_000, nameof(TimeBudgetMs));
            var minD = ClampWarn(MinSearchDepth, 1, 20, nameof(MinSearchDepth));
            var maxD = ClampWarn(MaxSearchDepth, minD, 20, nameof(MaxSearchDepth));
            var topN = ClampWarn(TopCandidateCount, 1, 100, nameof(TopCandidateCount));
            var noise = ClampWarn(Noise, 0f, 1f, nameof(Noise));
            var risk = ClampWarn(RiskBias, -1f, 1f, nameof(RiskBias));

            var weights = new EvaluationWeights(
                Mathf.Max(0f, AttackWeight),
                Mathf.Max(0f, DefenseWeight),
                Mathf.Max(0f, CenterWeight),
                Mathf.Max(0f, IntersectionWeight));

            return new BotProfileData(
                winP, blockP, budget, minD, maxD,
                topN, noise, risk, weights, EnableDiagnostics);
        }

        private static float ClampWarn(float value, float min, float max, string fieldName)
        {
            if (value >= min && value <= max) return value;
            Debug.LogWarning($"[BotProfile] {fieldName}={value} out of range [{min}..{max}], clamped.");
            return Mathf.Clamp(value, min, max);
        }

        private static int ClampWarn(int value, int min, int max, string fieldName)
        {
            if (value >= min && value <= max) return value;
            Debug.LogWarning($"[BotProfile] {fieldName}={value} out of range [{min}..{max}], clamped.");
            return Mathf.Clamp(value, min, max);
        }
    }
}
