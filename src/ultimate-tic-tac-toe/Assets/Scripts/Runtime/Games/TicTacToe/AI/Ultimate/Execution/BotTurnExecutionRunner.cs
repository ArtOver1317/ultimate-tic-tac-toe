#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Runtime.Gameplay;
using Runtime.Games.TicTacToe.AI.Ultimate.Core;

namespace Runtime.Games.TicTacToe.AI.Ultimate.Execution
{
    internal readonly struct BotTurnExecutionFailure
    {
        public BotFailureReason Reason { get; }
        public string Message { get; }
        public string ErrorKey { get; }

        public BotTurnExecutionFailure(BotFailureReason reason, string? message, string? errorKey)
        {
            Reason = reason;
            Message = message ?? string.Empty;
            ErrorKey = errorKey ?? string.Empty;
        }
    }

    internal readonly struct BotTurnExecutionResult
    {
        public BotTurnId? SubmittedTurnId { get; }
        public BotDecisionDiagnostics? Diagnostics { get; }
        public BotTurnExecutionFailure? Failure { get; }

        public bool HasSubmittedTurnId => SubmittedTurnId.HasValue;
        public bool HasFailure => Failure.HasValue;

        private BotTurnExecutionResult(
            BotTurnId? submittedTurnId,
            BotDecisionDiagnostics? diagnostics,
            BotTurnExecutionFailure? failure)
        {
            SubmittedTurnId = submittedTurnId;
            Diagnostics = diagnostics;
            Failure = failure;
        }

        public static BotTurnExecutionResult NoAction() => new(null, null, null);

        public static BotTurnExecutionResult Submitted(BotTurnId submittedTurnId, BotDecisionDiagnostics? diagnostics)
            => new(submittedTurnId, diagnostics, null);

        public static BotTurnExecutionResult Failed(BotTurnExecutionFailure failure, BotDecisionDiagnostics? diagnostics)
            => new(null, diagnostics, failure);
    }

    internal sealed class BotTurnExecutionRunner
    {
        private readonly IGameplaySnapshotProvider _snapshot;
        private readonly IUltimateBotStateReader _stateReader;
        private readonly IUltimateBotDecisionEngine _engine;
        private readonly IBotMoveCommandSink _commandSink;

        public BotTurnExecutionRunner(
            IGameplaySnapshotProvider snapshot,
            IUltimateBotStateReader stateReader,
            IUltimateBotDecisionEngine engine,
            IBotMoveCommandSink commandSink)
        {
            _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            _stateReader = stateReader ?? throw new ArgumentNullException(nameof(stateReader));
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _commandSink = commandSink ?? throw new ArgumentNullException(nameof(commandSink));
        }

        public async UniTask<BotTurnExecutionResult> ExecuteAsync(
            int botSlot,
            BotTurnId turnId,
            UltimateBotDifficultyProfileData profile,
            IBotRngSession rng,
            CancellationToken ct)
        {
            if (!_stateReader.TryBuildDecisionRequest(
                    botSlot,
                    turnId,
                    profile,
                    rng,
                    out var request,
                    out var failReason))
            {
                return failReason == BotFailureReason.NoLegalMovesInconsistentState
                    ? BotTurnExecutionResult.Failed(
                        new BotTurnExecutionFailure(
                            BotFailureReason.NoLegalMovesInconsistentState,
                            "No legal moves for in-progress state.",
                            "Errors.Bot.NoLegalMoves"),
                        diagnostics: null)
                    : BotTurnExecutionResult.NoAction();
            }

            if (profile.PreMoveDelayMs > 0) 
                await UniTask.Delay(profile.PreMoveDelayMs, cancellationToken: ct);

            var decisionResult = await _engine.ChooseMoveAsync(request, ct);
            
            if (ct.IsCancellationRequested) 
                return BotTurnExecutionResult.NoAction();

            var diagnostics = BuildDiagnostics(profile, decisionResult);
            
            if (_commandSink.TrySubmitMove(decisionResult.Move, turnId)) 
                return BotTurnExecutionResult.Submitted(turnId, diagnostics);

            return await RetrySubmitAsync(botSlot, turnId, profile, rng, diagnostics, ct);
        }

        private async UniTask<BotTurnExecutionResult> RetrySubmitAsync(
            int botSlot,
            BotTurnId turnId,
            UltimateBotDifficultyProfileData profile,
            IBotRngSession rng,
            BotDecisionDiagnostics? diagnostics,
            CancellationToken ct)
        {
            if (!_stateReader.TryBuildDecisionRequest(
                    botSlot,
                    BotTurnId.Build(_snapshot.CommandSequence, _snapshot.ActivePlayerSlot),
                    profile,
                    rng,
                    out var retryRequest,
                    out _))
            {
                return BotTurnExecutionResult.Failed(
                    new BotTurnExecutionFailure(
                        BotFailureReason.EngineError,
                        "Retry snapshot unavailable.",
                        "Errors.Bot.RetrySnapshotUnavailable"),
                    diagnostics);
            }

            var retryResult = await _engine.ChooseMoveAsync(retryRequest, ct);
            
            if (_commandSink.TrySubmitMove(retryResult.Move, retryRequest.TurnId)) 
                return BotTurnExecutionResult.Submitted(retryRequest.TurnId, diagnostics);

            var fallback = retryRequest.LegalMovesStable.Count > 0
                ? retryRequest.LegalMovesStable[0]
                : default;

            if (_commandSink.TrySubmitMove(fallback, retryRequest.TurnId)) 
                return BotTurnExecutionResult.Submitted(retryRequest.TurnId, diagnostics);

            return BotTurnExecutionResult.Failed(
                new BotTurnExecutionFailure(
                    BotFailureReason.EngineError,
                    "Submit retry policy exhausted.",
                    "Errors.Bot.SubmitFailed"),
                diagnostics: null);
        }

        private static BotDecisionDiagnostics? BuildDiagnostics(
            UltimateBotDifficultyProfileData profile,
            UltimateBotDecisionResult decisionResult)
        {
            if (!profile.EnableDiagnostics) return null;

            return new BotDecisionDiagnostics(
                decisionResult.SearchDepthReached,
                decisionResult.IterationsCompleted,
                decisionResult.DegradationReason);
        }
    }
}