using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.GameModes.Wizard.Modes;
using Runtime.Gameplay;
using Runtime.Games.Battleship.UI;
using Runtime.Games.TicTacToe.Ultimate.UI;
using Runtime.Infrastructure.Logging;
using Runtime.Localization;
using Runtime.Localization.Contracts;
using Runtime.Localization.Types;
using StripLog;
using UnityEngine;
using UnityEngine.UIElements;

namespace Runtime.Games.TicTacToe
{
    public sealed class GameplayFieldPresenter : IGameplayFieldPresenter, IGameplayFieldUiAdapter, IUltimateGameplayFieldUiAdapter, IBattleshipFieldUiAdapter
    {
        private const int _battleshipBoardSize = 10;

        private readonly IGameplayBackHandler _backHandler;
        private readonly ILocalizationService _localization;
        private readonly Subject<CellId> _cellClicks = new();
        private readonly Subject<CellId> _ownBoardCellClicks = new();
        private readonly GameplayFieldPresenterState _state = new();
        private readonly GameplayFieldPresenterScoreboardBuilder _scoreboardBuilder;
        private readonly GameplayFieldPresenterFieldBuilder _fieldBuilder;
        private readonly GameplayFieldPresenterLayoutController _layoutController;

        private bool _disposed;
        private bool _backInProgress;

        public GameplayFieldPresenter(
            UIDocument uiDocument,
            IGameplayBackHandler backHandler,
            ILocalizationService localization = null)
        {
            var document = uiDocument ? uiDocument : throw new ArgumentNullException(nameof(uiDocument));
            _backHandler = backHandler ?? throw new ArgumentNullException(nameof(backHandler));
            _localization = localization;
            _scoreboardBuilder = new GameplayFieldPresenterScoreboardBuilder(_state);
            _layoutController = new GameplayFieldPresenterLayoutController(_state);

            var fieldContext = new GameplayFieldPresenterFieldContext(
                IsReady,
                OnBackClicked,
                PublishCellClick,
                PublishOwnBoardCellClick,
                ResolveGameTextOrFallback);

            _fieldBuilder = new GameplayFieldPresenterFieldBuilder(
                document,
                _state,
                _scoreboardBuilder,
                _layoutController,
                fieldContext);
        }

        Observable<CellId> IGameplayFieldUiAdapter.CellClicks => _cellClicks;
        Observable<CellId> IBattleshipFieldUiAdapter.OwnBoardCellClicks => _ownBoardCellClicks;

        bool IBattleshipFieldUiAdapter.HasOwnBoard =>
            IsReady()
            && _state.CurrentMode == GameplayFieldPresenterMode.BattleshipDual;

        bool IGameplayFieldUiAdapter.TryGetCellView(CellId id, out VisualElement cellRoot, out Label markLabel)
        {
            cellRoot = null;
            markLabel = null;

            if (!TryGetCell(id, out cellRoot) || cellRoot == null)
                return false;

            return _state.MarkLabelById.TryGetValue(id, out markLabel) && markLabel != null;
        }

        Label IGameplayFieldUiAdapter.CurrentPlayerLabel
        {
            get
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(GameplayFieldPresenter));

                if (!_state.IsBound)
                    throw new InvalidOperationException("GameplayFieldPresenter is not bound (CurrentPlayerLabel is unavailable).");

                if (_state.CurrentPlayerLabel != null)
                    return _state.CurrentPlayerLabel;

                _scoreboardBuilder.EnsureCurrentPlayerLabelExists();
                return _state.CurrentPlayerLabel;
            }
        }

        bool IGameplayFieldUiAdapter.TryGetCell(CellId id, out VisualElement cellRoot) =>
            TryGetCell(id, out cellRoot);

        bool IGameplayFieldUiAdapter.TryGetMark(CellId id, out VisualElement mark) =>
            TryGetMark(id, out mark);

        bool IBattleshipFieldUiAdapter.TryGetOwnCell(CellId id, out VisualElement cellRoot)
        {
            if (!IsReady() || _state.CurrentMode != GameplayFieldPresenterMode.BattleshipDual)
            {
                cellRoot = null;
                return false;
            }

            return _state.OwnBoardCellById.TryGetValue(id, out cellRoot) && cellRoot != null;
        }

        bool IBattleshipFieldUiAdapter.TryGetOwnCellView(CellId id, out VisualElement cellRoot, out Label markLabel)
        {
            cellRoot = null;
            markLabel = null;

            if (!IsReady() || _state.CurrentMode != GameplayFieldPresenterMode.BattleshipDual)
                return false;

            if (!_state.OwnBoardCellById.TryGetValue(id, out cellRoot) || cellRoot == null)
                return false;

            return _state.OwnBoardMarkLabelById.TryGetValue(id, out markLabel) && markLabel != null;
        }

        bool IUltimateGameplayFieldUiAdapter.TryGetMiniBoard(int major, out VisualElement miniBoardRoot)
        {
            if (!IsReady())
            {
                miniBoardRoot = null;
                return false;
            }

            return _state.MiniBoardByMajor.TryGetValue(major, out miniBoardRoot) && miniBoardRoot != null;
        }

        bool IUltimateGameplayFieldUiAdapter.TryGetMiniBoardCenter(int major, out Vector2 panelSpaceCenter)
        {
            if (!IsReady())
            {
                panelSpaceCenter = default;
                return false;
            }

            // Always prefer live worldBound to avoid stale cache when called at game-end
            // (GeometryChangedEvent order is not guaranteed relative to win detection).
            if (_state.MiniBoardByMajor.TryGetValue(major, out var mini) && mini != null)
            {
                var rect = mini.worldBound;
                if (rect.width > 0f && rect.height > 0f)
                {
                    panelSpaceCenter = rect.center;
                    return true;
                }
            }

            return _state.MiniBoardCenterByMajor.TryGetValue(major, out panelSpaceCenter);
        }

        VisualElement IGameplayFieldUiAdapter.FieldContainer => _state.IsBound ? _state.FieldContainer : null;

        VisualElement IGameplayFieldUiAdapter.Player1Panel => _state.IsBound ? _state.Player1Panel : null;
        VisualElement IGameplayFieldUiAdapter.Player2Panel => _state.IsBound ? _state.Player2Panel : null;
        Label IGameplayFieldUiAdapter.Player1ScoreLabel => _state.IsBound ? _state.Player1ScoreLabel : null;
        Label IGameplayFieldUiAdapter.Player1NameLabel => _state.IsBound ? _state.Player1NameLabel : null;
        Label IGameplayFieldUiAdapter.Player2ScoreLabel => _state.IsBound ? _state.Player2ScoreLabel : null;
        Label IGameplayFieldUiAdapter.Player2NameLabel => _state.IsBound ? _state.Player2NameLabel : null;
        Label IGameplayFieldUiAdapter.DrawsScoreLabel => _state.IsBound ? _state.DrawsScoreLabel : null;
        Label IGameplayFieldUiAdapter.MoveTimerLabel => _state.IsBound ? _state.MoveTimerLabel : null;

        public UniTask BindAsync(FieldRenderSpec spec, CancellationToken ct, string gameId = null)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(GameplayFieldPresenter));
            
            if (spec == null)
                throw new ArgumentNullException(nameof(spec));

            ct.ThrowIfCancellationRequested();

            if (_state.IsBound)
                Unbind();

            _state.Spec = spec;
            _state.CurrentMode = ResolveFieldMode(spec, gameId);
            _state.ResetStyleTokenState();

            try
            {
                _fieldBuilder.Build();
                _state.IsBound = true;
                _state.BindCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                _layoutController.UpdateCellSizes(_state.FieldContainer.contentRect);
            }
            catch
            {
                CleanupBindings();
                _state.BindCts?.Cancel();
                _state.BindCts?.Dispose();
                _state.BindCts = null;
                _state.Spec = null;
                _state.CurrentMode = default;
                _state.CurrentPlayerLabel = null;
                _state.IsBound = false;
                throw;
            }

            return UniTask.CompletedTask;
        }

        public void Unbind()
        {
            if (!_state.IsBound)
                return;

            CleanupBindings();

            _state.ClearCellCaches();
            _state.FieldContainer?.Clear();
            _state.BattleshipBoardsRoot = null;
            _state.BackButton = null;
            _state.Spec = null;
            _state.CurrentMode = default;
            _state.CurrentPlayerLabel = null;
            _state.Player1Panel = null;
            _state.Player2Panel = null;
            _state.Player1ScoreLabel = null;
            _state.Player2ScoreLabel = null;
            _state.Player1NameLabel = null;
            _state.Player2NameLabel = null;
            _state.DrawsScoreLabel = null;
            _state.MoveTimerLabel = null;
            _state.ResetStyleTokenState();
            _state.BindCts?.Cancel();
            _state.BindCts?.Dispose();
            _state.BindCts = null;
            _state.IsBound = false;
            _state.LastCellSize = 0;
        }

        private void CleanupBindings()
        {
            _layoutController.CleanupBindings();

            if (_state.BackButton != null)
                _state.BackButton.clicked -= OnBackClicked;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Unbind();

            _cellClicks.OnCompleted();
            _cellClicks.Dispose();
            _ownBoardCellClicks.OnCompleted();
            _ownBoardCellClicks.Dispose();
        }

        internal bool TryGetCell(CellId id, out VisualElement cellRoot) =>
            _fieldBuilder.TryGetCell(id, out cellRoot);

        private bool TryGetMark(CellId id, out VisualElement mark) =>
            _fieldBuilder.TryGetMark(id, out mark);

        internal void EmitCellClick(CellId cellId) =>
            PublishCellClick(cellId);

        internal void OnCellClicked(VisualElement cell) =>
            _fieldBuilder.OnCellClicked(cell);

        private void PublishCellClick(CellId cellId)
        {
            if (!IsReady())
                return;

            _cellClicks.OnNext(cellId);
        }

        private void PublishOwnBoardCellClick(CellId cellId)
        {
            if (!IsReady())
                return;

            _ownBoardCellClicks.OnNext(cellId);
        }

        private bool IsReady() => _state.IsBound && !_disposed;

        private static GameplayFieldPresenterMode ResolveFieldMode(FieldRenderSpec spec, string gameId)
        {
            if (spec.Kind == FieldKind.Ultimate)
                return GameplayFieldPresenterMode.Ultimate;

            return spec.OuterSize == _battleshipBoardSize
                   && string.Equals(gameId, BattleshipStrategy.DefaultGameId, StringComparison.Ordinal)
                ? GameplayFieldPresenterMode.BattleshipDual
                : GameplayFieldPresenterMode.Classic;
        }

        private void OnBackClicked()
        {
            if (_backInProgress)
                return;

            _backInProgress = true;
            BackToModeSelectionAsync(CancellationToken.None).Forget();
        }

        private async UniTask BackToModeSelectionAsync(CancellationToken ct)
        {
            try
            {
                await _backHandler.HandleBackAsync(ct);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Log.Error(LogTags.UI, $"[GameplayFieldPresenter] Failed to return to ModeSelection: {ex}");
            }
            finally
            {
                _backInProgress = false;
            }
        }

        private string ResolveGameTextOrFallback(string key, string fallback)
        {
            if (_localization == null || string.IsNullOrWhiteSpace(key))
                return fallback;

            var resolved = _localization.Resolve("Game", key);
            
            if (string.IsNullOrWhiteSpace(resolved) || string.Equals(resolved, key, StringComparison.Ordinal))
                return fallback;

            return resolved;
        }
    }
}