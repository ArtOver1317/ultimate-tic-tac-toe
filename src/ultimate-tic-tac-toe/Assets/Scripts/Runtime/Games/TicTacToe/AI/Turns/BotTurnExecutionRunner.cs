#nullable enable

using System.Threading;
using Cysharp.Threading.Tasks;
using Runtime.Gameplay;
using Runtime.Gameplay.Shared;
using Runtime.Games.TicTacToe.AI.Core;
using Runtime.Games.TicTacToe.AI.Profiles;
using Runtime.Games.TicTacToe.Moves;
using UnityEngine;

namespace Runtime.Games.TicTacToe.AI.Turns
{
    internal enum BotTurnExecutionStatus
    {
        NoAction,
        Completed,
        DisableBot,
    }

    internal sealed class BotTurnExecutionRunner
    {
        private enum TurnSubmissionResult
        {
            NoAction,
            Submitted,
            Rejected,
        }

        private readonly IMatchStateProvider _matchState;
        private readonly IBotDecisionEngine _engine;
        private readonly BotTurnRequestBuilder _requestBuilder;

        public BotTurnExecutionRunner(
            IMatchStateProvider matchState,
            IBotDecisionEngine engine,
            BotTurnRequestBuilder requestBuilder)
        {
            _matchState = matchState;
            _engine = engine;
            _requestBuilder = requestBuilder;
        }

        public async UniTask<BotTurnExecutionStatus> ExecuteAsync(
            int botSlot,
            BotProfile profile,
            BotProfileData profileData,
            CancellationToken ct)
        {
            await ApplyPreMoveDelayAsync(profile, ct);

            ct.ThrowIfCancellationRequested();

            var computedAttempt = await TrySubmitComputedMoveWithRetryAsync(botSlot, profileData, ct);
            
            if (computedAttempt == TurnSubmissionResult.NoAction)
                return BotTurnExecutionStatus.NoAction;

            if (computedAttempt == TurnSubmissionResult.Submitted)
                return BotTurnExecutionStatus.Completed;

            Debug.LogError("[BotTurnDriver] Retry rejected. Attempting deterministic fallback...");

            var fallbackAttempt = TrySubmitFallbackMove();
            
            if (fallbackAttempt == TurnSubmissionResult.Submitted)
            {
                Debug.LogError("[BotTurnDriver] Fallback move accepted — investigate why computed moves were rejected.");
                return BotTurnExecutionStatus.Completed;
            }

            return fallbackAttempt == TurnSubmissionResult.NoAction
                ? BotTurnExecutionStatus.NoAction
                : BotTurnExecutionStatus.DisableBot;
        }

        private async UniTask<TurnSubmissionResult> TrySubmitComputedMoveWithRetryAsync(
            int botSlot,
            BotProfileData profileData,
            CancellationToken ct)
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var result = await TryComputeAndSubmitAsync(
                    botSlot,
                    profileData,
                    ct,
                    logStaleValidation: attempt == 0);

                if (result != TurnSubmissionResult.Rejected)
                    return result;

                if (attempt == 0)
                    Debug.LogWarning("[BotTurnDriver] Command rejected. Retrying with fresh snapshot...");
            }

            return TurnSubmissionResult.Rejected;
        }

        private async UniTask<TurnSubmissionResult> TryComputeAndSubmitAsync(
            int botSlot,
            BotProfileData profileData,
            CancellationToken ct,
            bool logStaleValidation)
        {
            if (!_requestBuilder.TryBuild(out var request))
                return TurnSubmissionResult.NoAction;

            var move = await _engine.ChooseMoveAsync(request, profileData, ct);
            ct.ThrowIfCancellationRequested();

            if (!ValidatePreSubmit(botSlot, request.CommandSequence))
            {
                if (logStaleValidation)
                    Debug.LogWarning("[BotTurnDriver] Pre-submit validation failed (stale). Discarding move.");

                return TurnSubmissionResult.NoAction;
            }

            return TrySubmitMove(move)
                ? TurnSubmissionResult.Submitted
                : TurnSubmissionResult.Rejected;
        }

        private TurnSubmissionResult TrySubmitFallbackMove()
        {
            if (!_requestBuilder.TryBuild(out var request) || request.LegalMoves.Count == 0)
                return TurnSubmissionResult.NoAction;

            return TrySubmitMove(request.LegalMoves[0])
                ? TurnSubmissionResult.Submitted
                : TurnSubmissionResult.Rejected;
        }

        private static async UniTask ApplyPreMoveDelayAsync(BotProfile profile, CancellationToken ct)
        {
            if (profile.PreMoveDelay > 0)
                await UniTask.Delay(profile.PreMoveDelay, cancellationToken: ct);
        }

        private bool ValidatePreSubmit(int botSlot, long requestCommandSequence)
        {
            if (!_matchState.IsMatchActive)
                return false;

            if (_matchState.ActivePlayerSlot != botSlot)
                return false;

            return _matchState.CommandSequence == requestCommandSequence;
        }

        private bool TrySubmitMove(CellId move)
        {
            var commandSequenceBeforeSubmit = _matchState.CommandSequence;
            _matchState.SubmitCommand(new MakeMoveCommand(move));
            return _matchState.CommandSequence > commandSequenceBeforeSubmit;
        }
    }
}