#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.Gameplay;
using Runtime.Games.TicTacToe.Moves;
using Runtime.Games.TicTacToe.Rules;
using Runtime.Games.TicTacToe.Ultimate.Rules;

namespace Runtime.Games.TicTacToe.AI.Ultimate
{
    public enum BotOrchestratorState
    {
        NotStarted,
        Active,
        Stopped,
        Disposed,
    }

    public enum BotFailureReason
    {
        TimeoutBest,
        TimeoutFallbackLegal,
        NoLegalMovesInconsistentState,
        Cancelled,
        EngineError,
    }

    public enum HardRuleType
    {
        GlobalWinNow,
        GlobalBlockNow,
        LocalWinNow,
        LocalBlockNow,
    }

    public enum SearchCutoffReason
    {
        Completed,
        TimeBudgetExceeded,
        NodeCapExceeded,
        Cancelled,
    }

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

    public readonly struct UltimateBoardSnapshot
    {
        public ReadOnlyMemory<PlayerMark> Cells81 { get; }
        public ReadOnlyMemory<MiniBoardStatus> MiniBoards9 { get; }
        public AllowedMajors AllowedMajors { get; }
        public int ActivePlayerSlot { get; }
        public CellId LastMoveOrDefault { get; }
        public bool HasLastMove { get; }
        public GameStatus MatchStatus { get; }

        public UltimateBoardSnapshot(
            ReadOnlyMemory<PlayerMark> cells81,
            ReadOnlyMemory<MiniBoardStatus> miniBoards9,
            AllowedMajors allowedMajors,
            int activePlayerSlot,
            CellId lastMoveOrDefault,
            bool hasLastMove,
            GameStatus matchStatus)
        {
            Cells81 = cells81;
            MiniBoards9 = miniBoards9;
            AllowedMajors = allowedMajors;
            ActivePlayerSlot = activePlayerSlot;
            LastMoveOrDefault = lastMoveOrDefault;
            HasLastMove = hasLastMove;
            MatchStatus = matchStatus;
        }
    }

    public readonly struct UltimateBotDecisionRequest
    {
        public BotTurnId TurnId { get; }
        public UltimateBoardSnapshot Snapshot { get; }
        public IReadOnlyList<CellId> LegalMovesStable { get; }
        public UltimateBotDifficultyProfileData Profile { get; }
        public IBotRngSession Rng { get; }

        public UltimateBotDecisionRequest(
            BotTurnId turnId,
            UltimateBoardSnapshot snapshot,
            IReadOnlyList<CellId> legalMovesStable,
            UltimateBotDifficultyProfileData profile,
            IBotRngSession rng)
        {
            TurnId = turnId;
            Snapshot = snapshot;
            LegalMovesStable = legalMovesStable ?? throw new ArgumentNullException(nameof(legalMovesStable));
            Profile = profile;
            Rng = rng ?? throw new ArgumentNullException(nameof(rng));
        }
    }

    public readonly struct UltimateBotDecisionResult
    {
        public CellId Move { get; }
        public BotFailureReason? DegradationReason { get; }
        public bool HardRuleApplied { get; }
        public HardRuleType? AppliedHardRule { get; }
        public int EvaluatedNodes { get; }
        public SearchCutoffReason CutoffReason { get; }
        public string CutoffDetails { get; }
        public int SearchDepthReached { get; }
        public int IterationsCompleted { get; }
        public int EvaluatedCandidates { get; }

        public UltimateBotDecisionResult(
            CellId move,
            BotFailureReason? degradationReason,
            bool hardRuleApplied,
            HardRuleType? appliedHardRule,
            int evaluatedNodes,
            SearchCutoffReason cutoffReason,
            string cutoffDetails,
            int searchDepthReached,
            int iterationsCompleted,
            int evaluatedCandidates)
        {
            Move = move;
            DegradationReason = degradationReason;
            HardRuleApplied = hardRuleApplied;
            AppliedHardRule = appliedHardRule;
            EvaluatedNodes = evaluatedNodes;
            CutoffReason = cutoffReason;
            CutoffDetails = cutoffDetails ?? string.Empty;
            SearchDepthReached = searchDepthReached;
            IterationsCompleted = iterationsCompleted;
            EvaluatedCandidates = evaluatedCandidates;
        }
    }

    public readonly struct BotMoveFailedEvent
    {
        public BotTurnId TurnId { get; }
        public BotFailureReason Reason { get; }
        public string Message { get; }

        public BotMoveFailedEvent(BotTurnId turnId, BotFailureReason reason, string message)
        {
            TurnId = turnId;
            Reason = reason;
            Message = message ?? string.Empty;
        }
    }

    public readonly struct DuplicateTurnIgnoredEvent
    {
        public BotTurnId TurnId { get; }
        public string ReasonCode { get; }

        public DuplicateTurnIgnoredEvent(BotTurnId turnId, string reasonCode)
        {
            TurnId = turnId;
            ReasonCode = reasonCode ?? string.Empty;
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

    public readonly struct BotDecisionDiagnostics
    {
        public BotTurnId TurnId { get; }
        public int DepthReached { get; }
        public int IterationCount { get; }
        public IReadOnlyList<BotCandidateScore> TopCandidates { get; }
        public BotFailureReason? DegradationReason { get; }

        public BotDecisionDiagnostics(
            BotTurnId turnId,
            int depthReached,
            int iterationCount,
            IReadOnlyList<BotCandidateScore> topCandidates,
            BotFailureReason? degradationReason)
        {
            TurnId = turnId;
            DepthReached = depthReached;
            IterationCount = iterationCount;
            TopCandidates = topCandidates ?? throw new ArgumentNullException(nameof(topCandidates));
            DegradationReason = degradationReason;
        }
    }

    public interface IBotRngSession
    {
        uint NextUInt();
        float NextFloat01();
        int NextInt(int minInclusive, int maxExclusive);
    }

    public interface IBotRandomizer
    {
        void Shuffle<T>(IList<T> values, IBotRngSession rng);
        int WeightedChoiceIndex(IReadOnlyList<float> weights, IBotRngSession rng);
    }

    public interface IBotRngSessionFactory
    {
        IBotRngSession Create(string matchInstanceId, int botSlot, UltimateBotDifficultyProfileData profile);
    }

    public interface IUltimateBotProfileCatalog
    {
        bool TryGet(string difficultyId, out UltimateBotDifficultyProfileData profile);
    }

    public interface IUltimateBotStateReader
    {
        bool TryBuildDecisionRequest(
            int botSlot,
            BotTurnId turnId,
            UltimateBotDifficultyProfileData profile,
            IBotRngSession rng,
            out UltimateBotDecisionRequest request,
            out BotFailureReason? failReason);
    }

    public interface IUltimateBotDecisionEngine
    {
        UniTask<UltimateBotDecisionResult> ChooseMoveAsync(UltimateBotDecisionRequest request, CancellationToken ct);
    }

    public interface IBotMoveCommandSink
    {
        bool TrySubmitMove(CellId move, BotTurnId turnId);
    }

    public interface IMatchFailSafeGateway
    {
        bool TryEnterAbortState(string userSafeMessageKey);
        void ResetAbortState();
        bool IsInputLocked { get; }
    }

    public interface IBotTurnOrchestrator : IDisposable
    {
        BotOrchestratorState State { get; }

        ReadOnlyReactiveProperty<bool> IsStarted { get; }
        ReadOnlyReactiveProperty<bool> IsThinking { get; }
        ReadOnlyReactiveProperty<BotTurnId?> InFlightTurnId { get; }
        ReadOnlyReactiveProperty<BotTurnId?> LastSubmittedTurnId { get; }

        Observable<BotMoveFailedEvent> MoveFailed { get; }
        Observable<DuplicateTurnIgnoredEvent> DuplicateIgnored { get; }
        Observable<BotDecisionDiagnostics> Diagnostics { get; }

        UniTask StartAsync(int botSlot, string difficultyId, CancellationToken ct);
        void Stop();
        UniTask TriggerIfBotTurnAsync(CancellationToken ct);
    }

    public readonly struct SelfPlaySeriesConfig
    {
        public string LeftProfileId { get; }
        public string RightProfileId { get; }
        public int Matches { get; }
        public int BaseSeed { get; }
        public int SeedCount { get; }

        public SelfPlaySeriesConfig(string leftProfileId, string rightProfileId, int matches, int baseSeed, int seedCount)
        {
            LeftProfileId = leftProfileId ?? throw new ArgumentNullException(nameof(leftProfileId));
            RightProfileId = rightProfileId ?? throw new ArgumentNullException(nameof(rightProfileId));
            Matches = matches;
            BaseSeed = baseSeed;
            SeedCount = seedCount;
        }
    }

    public readonly struct SelfPlaySeriesReport
    {
        public DateTimeOffset StartedAtUtc { get; }
        public DateTimeOffset CompletedAtUtc { get; }
        public string SeedRangeLabel { get; }
        public string LeftProfileVersion { get; }
        public string RightProfileVersion { get; }
        public string LeftProfileHash { get; }
        public string RightProfileHash { get; }

        public int Matches { get; }
        public int WinsLeft { get; }
        public int WinsRight { get; }
        public int Draws { get; }

        public float AvgMoveMs { get; }
        public float P50MoveMs { get; }
        public float P95MoveMs { get; }

        public int MissedHardRuleCount { get; }
        public int TimeoutBestCount { get; }
        public int TimeoutFallbackLegalCount { get; }
        public int NoLegalMovesInconsistentStateCount { get; }

        public SelfPlaySeriesReport(
            DateTimeOffset startedAtUtc,
            DateTimeOffset completedAtUtc,
            string seedRangeLabel,
            string leftProfileVersion,
            string rightProfileVersion,
            string leftProfileHash,
            string rightProfileHash,
            int matches,
            int winsLeft,
            int winsRight,
            int draws,
            float avgMoveMs,
            float p50MoveMs,
            float p95MoveMs,
            int missedHardRuleCount,
            int timeoutBestCount,
            int timeoutFallbackLegalCount,
            int noLegalMovesInconsistentStateCount)
        {
            StartedAtUtc = startedAtUtc;
            CompletedAtUtc = completedAtUtc;
            SeedRangeLabel = seedRangeLabel ?? string.Empty;
            LeftProfileVersion = leftProfileVersion ?? string.Empty;
            RightProfileVersion = rightProfileVersion ?? string.Empty;
            LeftProfileHash = leftProfileHash ?? string.Empty;
            RightProfileHash = rightProfileHash ?? string.Empty;
            Matches = matches;
            WinsLeft = winsLeft;
            WinsRight = winsRight;
            Draws = draws;
            AvgMoveMs = avgMoveMs;
            P50MoveMs = p50MoveMs;
            P95MoveMs = p95MoveMs;
            MissedHardRuleCount = missedHardRuleCount;
            TimeoutBestCount = timeoutBestCount;
            TimeoutFallbackLegalCount = timeoutFallbackLegalCount;
            NoLegalMovesInconsistentStateCount = noLegalMovesInconsistentStateCount;
        }
    }

    public interface IBotSelfPlayRunner
    {
        UniTask<SelfPlaySeriesReport> RunAsync(SelfPlaySeriesConfig config, CancellationToken ct);
    }
}
