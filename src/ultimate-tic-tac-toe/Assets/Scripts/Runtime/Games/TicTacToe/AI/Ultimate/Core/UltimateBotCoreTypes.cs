#nullable enable

using System;
using Runtime.Games.TicTacToe.Moves;

namespace Runtime.Games.TicTacToe.AI.Ultimate.Core
{
    public readonly struct BotTurnId : IEquatable<BotTurnId>
    {
        public long CommandSequenceBeforeTurn { get; }
        public int ActivePlayerSlot { get; }

        public BotTurnId(long commandSequenceBeforeTurn, int activePlayerSlot)
        {
            CommandSequenceBeforeTurn = commandSequenceBeforeTurn;
            ActivePlayerSlot = activePlayerSlot;
        }

        public bool Equals(BotTurnId other)
            => CommandSequenceBeforeTurn == other.CommandSequenceBeforeTurn
               && ActivePlayerSlot == other.ActivePlayerSlot;

        public override bool Equals(object? obj)
            => obj is BotTurnId other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine(CommandSequenceBeforeTurn, ActivePlayerSlot);

        public static BotTurnId Build(long commandSequenceBeforeTurn, int activePlayerSlot)
            => new(commandSequenceBeforeTurn, activePlayerSlot);
    }

    public readonly struct EvaluationWeights : IEquatable<EvaluationWeights>
    {
        public float GlobalControlWeight { get; }
        public float GlobalThreatWeight { get; }
        public float LocalThreatWeight { get; }
        public float SteeringWeight { get; }
        public float FlexibilityWeight { get; }

        public EvaluationWeights(
            float globalControlWeight,
            float globalThreatWeight,
            float localThreatWeight,
            float steeringWeight,
            float flexibilityWeight)
        {
            GlobalControlWeight = globalControlWeight;
            GlobalThreatWeight = globalThreatWeight;
            LocalThreatWeight = localThreatWeight;
            SteeringWeight = steeringWeight;
            FlexibilityWeight = flexibilityWeight;
        }

        public static EvaluationWeights Default => new(1f, 1f, 1f, 0.75f, 0.5f);

        public bool Equals(EvaluationWeights other)
            => GlobalControlWeight.Equals(other.GlobalControlWeight)
               && GlobalThreatWeight.Equals(other.GlobalThreatWeight)
               && LocalThreatWeight.Equals(other.LocalThreatWeight)
               && SteeringWeight.Equals(other.SteeringWeight)
               && FlexibilityWeight.Equals(other.FlexibilityWeight);

        public override bool Equals(object? obj)
            => obj is EvaluationWeights other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine(
                GlobalControlWeight,
                GlobalThreatWeight,
                LocalThreatWeight,
                SteeringWeight,
                FlexibilityWeight);
    }

    public readonly struct UltimateBotDifficultyProfileData
    {
        public string ProfileId { get; }
        public string ProfileVersion { get; }
        public string ProfileHash { get; }

        public int TimeBudgetMs { get; }
        public int MinSearchDepth { get; }
        public int MaxSearchDepth { get; }
        public int MaxEvaluatedNodes { get; }
        public int TopCandidateCount { get; }
        public float Noise { get; }

        public float MustWinGlobalNowProbability { get; }
        public float MustBlockGlobalNowProbability { get; }
        public float MustWinLocalNowProbability { get; }
        public float MustBlockLocalNowProbability { get; }

        public bool UseSeed { get; }
        public int Seed { get; }
        public int PreMoveDelayMs { get; }
        public bool EnableDiagnostics { get; }

        public EvaluationWeights Weights { get; }

        public UltimateBotDifficultyProfileData(
            string profileId,
            string profileVersion,
            string profileHash,
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
            ProfileId = profileId ?? throw new ArgumentNullException(nameof(profileId));
            ProfileVersion = profileVersion ?? throw new ArgumentNullException(nameof(profileVersion));
            ProfileHash = profileHash ?? throw new ArgumentNullException(nameof(profileHash));
            TimeBudgetMs = timeBudgetMs;
            MinSearchDepth = minSearchDepth;
            MaxSearchDepth = maxSearchDepth;
            MaxEvaluatedNodes = maxEvaluatedNodes;
            TopCandidateCount = topCandidateCount;
            Noise = noise;
            MustWinGlobalNowProbability = mustWinGlobalNowProbability;
            MustBlockGlobalNowProbability = mustBlockGlobalNowProbability;
            MustWinLocalNowProbability = mustWinLocalNowProbability;
            MustBlockLocalNowProbability = mustBlockLocalNowProbability;
            UseSeed = useSeed;
            Seed = seed;
            PreMoveDelayMs = preMoveDelayMs;
            EnableDiagnostics = enableDiagnostics;
            Weights = weights;
        }
    }

    public readonly struct BotCandidateScore
    {
        public CellId Move { get; }
        public float Score { get; }

        public BotCandidateScore(CellId move, float score)
        {
            Move = move;
            Score = score;
        }
    }
}