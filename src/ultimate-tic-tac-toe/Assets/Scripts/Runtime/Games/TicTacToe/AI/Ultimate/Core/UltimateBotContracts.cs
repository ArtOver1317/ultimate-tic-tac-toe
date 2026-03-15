#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.Games.TicTacToe.Moves;

namespace Runtime.Games.TicTacToe.AI.Ultimate.Core
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

    public interface IBotRngSession
    {
        uint NextUInt();
        float NextFloat01();
        int NextInt(int minInclusive, int maxExclusive);
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
        ReadOnlyReactiveProperty<bool> IsThinking { get; }

        Observable<BotMoveFailedEvent> MoveFailed { get; }

        UniTask StartAsync(int botSlot, string difficultyId, CancellationToken ct);
    }
}
