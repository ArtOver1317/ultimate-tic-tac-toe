#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.Games.TicTacToe.Moves;
using Runtime.Infrastructure.Logging;

namespace Runtime.Games.TicTacToe.Rules
{
    public readonly struct RoundFinishedEvent
    {
        public GameResult Result { get; }
        public CellId LastMove { get; }

        public RoundFinishedEvent(GameResult result, CellId lastMove)
        {
            Result = result;
            LastMove = lastMove;
        }
    }

    /// <summary>
    /// Maintains a mirror-board, evaluates rules on each move, publishes RoundFinished.
    /// Subscribes directly to <see cref="ILocalMovesService.CellChanged"/>.
    /// </summary>
    public sealed class GameplayRulesHandler
    {
        private readonly IRulesEngine _rulesEngine;
        private readonly ILocalMovesService _localMoves;
        private readonly Subject<RoundFinishedEvent> _roundFinished = new();

        private PlayerMark[]? _board;
        private int _boardSize;
        private IDisposable? _subscription;
        private CancellationTokenSource? _deferCts;
        private bool _isRoundFinished;

        public Observable<RoundFinishedEvent> RoundFinished => _roundFinished;

        /// <summary>
        /// When false, round-finished events are published synchronously (for EditMode tests).
        /// Default is true — ADR-4: defer to next frame to protect subscribers from re-entrancy.
        /// </summary>
        internal bool DeferToNextFrame { get; set; } = true;

        public GameplayRulesHandler(IRulesEngine rulesEngine, ILocalMovesService localMoves)
        {
            _rulesEngine = rulesEngine ?? throw new ArgumentNullException(nameof(rulesEngine));
            _localMoves = localMoves ?? throw new ArgumentNullException(nameof(localMoves));
        }

        public void Bind(int boardSize)
        {
            if (boardSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(boardSize));

            Unbind();

            _boardSize = boardSize;

            // Reuse array if size matches (cold-path optimization).
            var totalCells = boardSize * boardSize;
            if (_board == null || _board.Length != totalCells)
                _board = new PlayerMark[totalCells];
            else
                Array.Clear(_board, 0, _board.Length);

            _isRoundFinished = false;

            _subscription = _localMoves.CellChanged.Subscribe(OnCellChanged);
        }

        public void Unbind()
        {
            _deferCts?.Cancel();
            _deferCts?.Dispose();
            _deferCts = null;

            _subscription?.Dispose();
            _subscription = null;
            _board = null;
            _isRoundFinished = false;
        }

        private void OnCellChanged(CellChangedEvent evt)
        {
            try
            {
                if (_isRoundFinished)
                    return;

                // Filter: ignore CellChanged(None) — fires during restart clear.
                if (evt.NewValue == PlayerMark.None)
                    return;

                if (_board == null)
                    return;

                int index = evt.CellId.Major * _boardSize + evt.CellId.Minor;
                if (index < 0 || index >= _board.Length)
                    return;

                _board[index] = evt.NewValue;

                var result = _rulesEngine.Evaluate(_board, _boardSize, evt.CellId);
                if (result.Status == GameStatus.InProgress)
                    return;

                _isRoundFinished = true;

                // ADR-4: defer to next frame so LocalMovesService finishes its event chain
                // before any subscriber acts on round-finished. Protects all subscribers
                // from re-entrancy, not just the startup.
                var roundEvt = new RoundFinishedEvent(result, evt.CellId);

                if (DeferToNextFrame)
                {
                    _deferCts?.Cancel();
                    _deferCts?.Dispose();
                    _deferCts = new CancellationTokenSource();
                    DeferPublishAsync(roundEvt, _deferCts.Token).Forget();
                }
                else
                {
                    // Sync path — used in EditMode tests where PlayerLoop is not available.
                    _roundFinished.OnNext(roundEvt);
                }
            }
            catch (Exception ex)
            {
                // Exception safety: log, don't interrupt the CellChanged event chain.
                GameLog.Error($"[GameplayRulesHandler] Error evaluating rules: {ex}");
            }
        }

        private async UniTaskVoid DeferPublishAsync(RoundFinishedEvent evt, CancellationToken ct)
        {
            try
            {
                await UniTask.NextFrame(ct);

                // Guard: if Unbind() was called during the frame delay, don't publish.
                if (_board == null || !_isRoundFinished)
                    return;

                _roundFinished.OnNext(evt);
            }
            catch (OperationCanceledException)
            {
                // Expected on Unbind — not an error.
            }
            catch (Exception ex)
            {
                GameLog.Error($"[GameplayRulesHandler] Error in deferred round-finished publish: {ex}");
            }
        }
    }
}
