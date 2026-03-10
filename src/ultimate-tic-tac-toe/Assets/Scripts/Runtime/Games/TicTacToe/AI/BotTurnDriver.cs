#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.Gameplay;
using Runtime.Gameplay.ECS;
using Runtime.GameModes.Wizard;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.Modes;
using Runtime.Games.TicTacToe.Moves;
using UnityEngine;

namespace Runtime.Games.TicTacToe.AI
{
    /// <summary>
    /// Runtime driver that listens to gameplay events and submits bot moves (ADR-1, ADR-6, ADR-10).
    /// One driver per bot slot. Dispose cancels any in-flight computation.
    /// </summary>
    public sealed class BotTurnDriver : IBotTurnDriver
    {
        private readonly IMatchStateProvider _matchState;
        private readonly IBotDecisionEngine _engine;
        private readonly IBotProfileCatalog _profileCatalog;
        private readonly IClassicWinLengthProvider _winLengthProvider;

        private readonly ReactiveProperty<bool> _isBusy = new(false);
        private readonly ReactiveProperty<bool> _isDisabled = new(false);
        private CompositeDisposable? _subscriptions;
        private CancellationTokenSource? _turnCts;
        private CancellationTokenSource? _lifetimeCts;

        private BotProfileData _profileData;
        private BotProfile? _profile;
        private IBotRandom? _rng;
        private PlayerMark[]? _cellsBuffer;
        private readonly List<CellId> _legalMovesBuffer = new();
        private int _botSlot;
        private int _boardSize;
        private int _winLength;
        private bool _started;
        private bool _botDisabled;
        private bool _disposed;

        public ReadOnlyReactiveProperty<bool> IsBusy => _isBusy;
        public ReadOnlyReactiveProperty<bool> IsDisabled => _isDisabled;

        public BotTurnDriver(
            IMatchStateProvider matchState,
            IBotDecisionEngine engine,
            IBotProfileCatalog profileCatalog,
            IClassicWinLengthProvider winLengthProvider)
        {
            _matchState = matchState ?? throw new ArgumentNullException(nameof(matchState));
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _profileCatalog = profileCatalog ?? throw new ArgumentNullException(nameof(profileCatalog));
            _winLengthProvider = winLengthProvider ?? throw new ArgumentNullException(nameof(winLengthProvider));
        }

        public UniTask<BotStartResult> StartAsync(
            GameLaunchConfig config, int botSlot, string difficultyId, CancellationToken ct)
        {
            if (_disposed)
                return UniTask.FromResult(new BotStartResult(BotStartStatus.Failed, "Driver disposed."));

            if (_started)
                return UniTask.FromResult(new BotStartResult(BotStartStatus.Failed, "Already started."));

            // ADR-2: Classic-only guard
            if (config.GameConfig is not TicTacToeConfig tttConfig)
                return UniTask.FromResult(
                    new BotStartResult(BotStartStatus.UnsupportedConfig, "Not a TicTacToe config."));

            if (tttConfig.IsUltimate)
                return UniTask.FromResult(
                    new BotStartResult(BotStartStatus.UnsupportedConfig, "Ultimate mode not supported by bot."));

            // Lookup profile
            if (!_profileCatalog.TryGet(difficultyId, out var profile) || profile == null)
                return UniTask.FromResult(
                    new BotStartResult(BotStartStatus.Failed, $"Profile '{difficultyId}' not found."));

            _profile = profile;
            _profileData = profile.ToValidatedData();
            _botSlot = botSlot;
            _boardSize = tttConfig.BoardSize;
            _winLength = _winLengthProvider.GetWinLength(_boardSize);
            _cellsBuffer = new PlayerMark[_boardSize * _boardSize];
            _legalMovesBuffer.Clear();
            _legalMovesBuffer.Capacity = Math.Max(_legalMovesBuffer.Capacity, _boardSize * _boardSize);
            _lifetimeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _started = true;

            // ADR-3: bot-local RNG — one instance per driver for determinism & isolation
            var seed = profile.UseFixedSeed
                ? unchecked(profile.FixedSeed + botSlot * 31337)
                : unchecked(Environment.TickCount + botSlot * 31337);
            _rng = new BotRandom(seed);

            Subscribe();

            // If it's already bot's turn, trigger move
            if (_matchState.IsMatchActive && _matchState.ActivePlayerSlot == _botSlot)
                TriggerTurnAsync().Forget();

            return UniTask.FromResult(new BotStartResult(BotStartStatus.Started));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            CancelCurrentTurn();
            _lifetimeCts?.Cancel();
            _lifetimeCts?.Dispose();
            _subscriptions?.Dispose();
            _isBusy.Dispose();
            _isDisabled.Dispose();
            _cellsBuffer = null;
            _legalMovesBuffer.Clear();
        }

        // ── Event subscriptions ──

        private void Subscribe()
        {
            _subscriptions?.Dispose();
            _subscriptions = new CompositeDisposable();

            _matchState.CurrentPlayerChanged
                .Subscribe(OnCurrentPlayerChanged)
                .AddTo(_subscriptions);

            _matchState.RoundFinished
                .Subscribe(OnRoundFinished)
                .AddTo(_subscriptions);
        }

        private void OnCurrentPlayerChanged(CurrentPlayerChangedEvent evt)
        {
            if (_disposed || _botDisabled) return;
            if (evt.ActivePlayerSlot != _botSlot) return;

            TriggerTurnAsync().Forget();
        }

        private void OnRoundFinished(RoundFinishedEvent _) => CancelCurrentTurn();

        // ── Turn execution ──

        private async UniTaskVoid TriggerTurnAsync()
        {
            if (_disposed || _botDisabled || !_matchState.IsMatchActive) return;

            // Single-flight (ADR-6): cancel previous
            CancelCurrentTurn();
            _turnCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts!.Token);
            var ct = _turnCts.Token;

            _isBusy.Value = true;
            try
            {
                // Yield to break synchronous call chain (important for BvB — prevents stack overflow)
                await UniTask.Yield(PlayerLoopTiming.Update, ct);

                await ExecuteTurnAsync(ct);
            }
            catch (OperationCanceledException)
            {
                // Normal: match ended or dispose during computation
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BotTurnDriver] Unexpected error during turn: {ex}");
            }
            finally
            {
                if (!_disposed)
                    _isBusy.Value = false;
            }
        }

        private async UniTask ExecuteTurnAsync(CancellationToken ct)
        {
            // PreMoveDelay (UX)
            if (_profile != null && _profile.PreMoveDelay > 0)
            {
                await UniTask.Delay(_profile.PreMoveDelay, cancellationToken: ct);
            }

            ct.ThrowIfCancellationRequested();

            // ── Attempt 1: compute + submit ──
            var request = BuildRequest();
            if (request == null) return;

            var chosenMove = await _engine.ChooseMoveAsync(request.Value, _profileData, ct);
            ct.ThrowIfCancellationRequested();

            if (!ValidatePreSubmit(request.Value.CommandSequence))
            {
                Debug.LogWarning("[BotTurnDriver] Pre-submit validation failed (stale). Discarding move.");
                return;
            }

            if (TrySubmitMove(chosenMove))
                return;

            // ── Attempt 2: retry with fresh snapshot (ADR-12) ──
            Debug.LogWarning("[BotTurnDriver] Command rejected. Retrying with fresh snapshot...");

            var retryRequest = BuildRequest();
            if (retryRequest == null) return;

            var retryMove = await _engine.ChooseMoveAsync(retryRequest.Value, _profileData, ct);
            ct.ThrowIfCancellationRequested();

            if (!ValidatePreSubmit(retryRequest.Value.CommandSequence))
                return;

            if (TrySubmitMove(retryMove))
                return;

            // ── Attempt 3: deterministic fallback — first legal move (ADR-12) ──
            Debug.LogError("[BotTurnDriver] Retry rejected. Attempting deterministic fallback...");

            var fallbackRequest = BuildRequest();
            if (fallbackRequest == null || fallbackRequest.Value.LegalMoves.Count == 0) return;

            var fallbackMove = fallbackRequest.Value.LegalMoves[0];
            if (TrySubmitMove(fallbackMove))
            {
                Debug.LogError("[BotTurnDriver] Fallback move accepted — investigate why computed moves were rejected.");
                return;
            }

            // ── All attempts exhausted — disable bot (ADR-12) ──
            _botDisabled = true;
            _isDisabled.Value = true;
            Debug.LogError("[BotTurnDriver] All attempts rejected. Bot disabled for remainder of match.");
        }

        // ── Move submission with synchronous rejection detection ──

        /// <summary>
        /// Submits a move command and returns true if accepted (not rejected).
        /// SubmitCommand triggers ECS Tick synchronously. If the move is accepted,
        /// CommandSequence increments; if rejected, it stays the same.
        /// This avoids dependency on event scheduling (DeferredEventScheduler).
        /// </summary>
        private bool TrySubmitMove(CellId move)
        {
            var seqBefore = _matchState.CommandSequence;
            _matchState.SubmitCommand(new MakeMoveCommand(move));
            return _matchState.CommandSequence > seqBefore;
        }

        // ── Snapshot → BotDecisionRequest ──

        private BotDecisionRequest? BuildRequest()
        {
            if (!_matchState.IsMatchActive) return null;
            if (_matchState.ActivePlayerSlot != _botSlot) return null;
            if (_cellsBuffer == null) return null;

            var allCells = _matchState.GetAllCells();
            var cells = _cellsBuffer;
            Array.Clear(cells, 0, cells.Length);
            _legalMovesBuffer.Clear();

            for (var i = 0; i < allCells.Count; i++)
            {
                var snap = allCells[i];
                var idx = snap.CellId.Major * _boardSize + snap.CellId.Minor;
                if (idx >= 0 && idx < cells.Length)
                {
                    cells[idx] = snap.Slot switch
                    {
                        0 => PlayerMark.X,
                        1 => PlayerMark.O,
                        _ => PlayerMark.None,
                    };
                }
            }

            // Collect legal moves: empty cells in row-major order (deterministic — ADR-12 fallback)
            for (var r = 0; r < _boardSize; r++)
            {
                for (var c = 0; c < _boardSize; c++)
                {
                    if (cells[r * _boardSize + c] == PlayerMark.None)
                        _legalMovesBuffer.Add(new CellId(r, c));
                }
            }

            if (_legalMovesBuffer.Count == 0) return null;

            return new BotDecisionRequest(
                _boardSize,
                _winLength,
                cells,
                _matchState.ActivePlayerSlot,
                _matchState.LastMove,
                _legalMovesBuffer,
                _matchState.CommandSequence,
                _rng!);
        }

        // ── Validation (ADR-6) ──

        /// <summary>
        /// All three pre-submit invariants:
        /// 1. Match active. 2. Bot's turn. 3. CommandSequence unchanged.
        /// </summary>
        private bool ValidatePreSubmit(long requestCommandSequence)
        {
            if (!_matchState.IsMatchActive) return false;
            if (_matchState.ActivePlayerSlot != _botSlot) return false;
            if (_matchState.CommandSequence != requestCommandSequence) return false;
            return true;
        }

        private void CancelCurrentTurn()
        {
            _turnCts?.Cancel();
            _turnCts?.Dispose();
            _turnCts = null;
        }
    }
}
