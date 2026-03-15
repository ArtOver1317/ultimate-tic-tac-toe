#nullable enable

using System;
using System.Collections.Generic;
using Runtime.Games.TicTacToe.Moves;
using Runtime.Games.TicTacToe.Ultimate.Rules;

namespace Runtime.Games.TicTacToe.AI.Ultimate.Core
{
    public readonly struct UltimateBoardSnapshot
    {
        public ReadOnlyMemory<PlayerMark> Cells81 { get; }
        public ReadOnlyMemory<MiniBoardStatus> MiniBoards9 { get; }
        public AllowedMajors AllowedMajors { get; }
        public int ActivePlayerSlot { get; }

        public UltimateBoardSnapshot(
            ReadOnlyMemory<PlayerMark> cells81,
            ReadOnlyMemory<MiniBoardStatus> miniBoards9,
            AllowedMajors allowedMajors,
            int activePlayerSlot)
        {
            Cells81 = cells81;
            MiniBoards9 = miniBoards9;
            AllowedMajors = allowedMajors;
            ActivePlayerSlot = activePlayerSlot;
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
        public SearchCutoffReason CutoffReason { get; }
        public string CutoffDetails { get; }
        public int SearchDepthReached { get; }
        public int IterationsCompleted { get; }

        public UltimateBotDecisionResult(
            CellId move,
            BotFailureReason? degradationReason,
            bool hardRuleApplied,
            HardRuleType? appliedHardRule,
            SearchCutoffReason cutoffReason,
            string? cutoffDetails,
            int searchDepthReached,
            int iterationsCompleted)
        {
            Move = move;
            DegradationReason = degradationReason;
            HardRuleApplied = hardRuleApplied;
            AppliedHardRule = appliedHardRule;
            CutoffReason = cutoffReason;
            CutoffDetails = cutoffDetails ?? string.Empty;
            SearchDepthReached = searchDepthReached;
            IterationsCompleted = iterationsCompleted;
        }
    }

    public readonly struct BotMoveFailedEvent
    {
        public BotFailureReason Reason { get; }
        public string Message { get; }

        public BotMoveFailedEvent(BotFailureReason reason, string? message)
        {
            Reason = reason;
            Message = message ?? string.Empty;
        }
    }

    public readonly struct DuplicateTurnIgnoredEvent { }

    public readonly struct BotDecisionDiagnostics
    {
        public int DepthReached { get; }
        public int IterationCount { get; }
        public BotFailureReason? DegradationReason { get; }

        public BotDecisionDiagnostics(
            int depthReached,
            int iterationCount,
            BotFailureReason? degradationReason)
        {
            DepthReached = depthReached;
            IterationCount = iterationCount;
            DegradationReason = degradationReason;
        }
    }
}