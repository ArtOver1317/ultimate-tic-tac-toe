#nullable enable

using System;

namespace Runtime.Games.TicTacToe.AI
{
    /// <summary>
    /// Evaluation heuristic weights for the bot decision engine.
    /// Immutable value type — no ScriptableObject dependency (ADR-9).
    /// </summary>
    public readonly struct EvaluationWeights : IEquatable<EvaluationWeights>
    {
        public float AttackWeight { get; }
        public float DefenseWeight { get; }
        public float CenterWeight { get; }
        public float IntersectionWeight { get; }

        public EvaluationWeights(float attackWeight, float defenseWeight, float centerWeight, float intersectionWeight)
        {
            AttackWeight = attackWeight;
            DefenseWeight = defenseWeight;
            CenterWeight = centerWeight;
            IntersectionWeight = intersectionWeight;
        }

        public static EvaluationWeights Default => new(1f, 1f, 0.5f, 0.5f);

        public bool Equals(EvaluationWeights other) =>
            AttackWeight.Equals(other.AttackWeight) &&
            DefenseWeight.Equals(other.DefenseWeight) &&
            CenterWeight.Equals(other.CenterWeight) &&
            IntersectionWeight.Equals(other.IntersectionWeight);

        public override bool Equals(object? obj) => obj is EvaluationWeights other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(AttackWeight, DefenseWeight, CenterWeight, IntersectionWeight);
    }

    /// <summary>
    /// Pure-data snapshot of a bot profile, validated and clamped (ADR-9).
    /// Created by <see cref="BotProfile.ToValidatedData"/>.
    /// Engine depends only on this — no UnityEngine.Object references.
    /// </summary>
    public readonly struct BotProfileData
    {
        public float MustWinNowProbability { get; }
        public float MustBlockNowProbability { get; }
        public int TimeBudgetMs { get; }
        public int MinSearchDepth { get; }
        public int MaxSearchDepth { get; }
        public int TopCandidateCount { get; }
        public float Noise { get; }
        public float RiskBias { get; }
        public EvaluationWeights Weights { get; }
        public bool EnableDiagnostics { get; }

        public BotProfileData(
            float mustWinNowProbability,
            float mustBlockNowProbability,
            int timeBudgetMs,
            int minSearchDepth,
            int maxSearchDepth,
            int topCandidateCount,
            float noise,
            float riskBias,
            EvaluationWeights weights,
            bool enableDiagnostics)
        {
            MustWinNowProbability = mustWinNowProbability;
            MustBlockNowProbability = mustBlockNowProbability;
            TimeBudgetMs = timeBudgetMs;
            MinSearchDepth = minSearchDepth;
            MaxSearchDepth = maxSearchDepth;
            TopCandidateCount = topCandidateCount;
            Noise = noise;
            RiskBias = riskBias;
            Weights = weights;
            EnableDiagnostics = enableDiagnostics;
        }
    }
}
