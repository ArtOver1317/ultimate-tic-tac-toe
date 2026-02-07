using System;
using System.Diagnostics;
using R3;
using Runtime.Infrastructure.Logging;
using UnityEngine.UIElements;

namespace Runtime.Gameplay.Moves
{
    public sealed class GameplayMovesBinder : IDisposable
    {
        private const string LastMoveClass = "cell--lastMove";
        private const string DisabledClass = "cell--disabled";

        private readonly IGameplayFieldUiAdapter _ui;
        private readonly ILocalMovesService _moves;

        private CompositeDisposable _subscriptions;
        private Label _currentPlayerLabel;
        private bool _isBound;
        private bool _disposed;

        public GameplayMovesBinder(
            IGameplayFieldUiAdapter ui,
            ILocalMovesService moves)
        {
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            _moves = moves ?? throw new ArgumentNullException(nameof(moves));
        }

        public void Bind()
        {
            ThrowIfDisposed();

            if (_isBound)
            {
                GameLog.Warning("[GameplayMovesBinder] Bind called more than once. Ignored.");

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.Assert(false, "GameplayMovesBinder.Bind() called more than once.");
#endif
                return;
            }

            if (!_moves.IsStarted.CurrentValue)
                GameLog.Warning("[GameplayMovesBinder] Bind called while moves service is not started. Clicks may be rejected.");

            try
            {
                _currentPlayerLabel = _ui.CurrentPlayerLabel;
            }
            catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
            {
                GameLog.Error($"[GameplayMovesBinder] Bind failed: UI is not ready ({ex.GetType().Name}). {ex.Message}");

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.Assert(false, "GameplayMovesBinder.Bind() failed: UI is not ready.");
#endif

                throw new InvalidOperationException("GameplayMovesBinder.Bind() failed: UI is not ready.", ex);
            }

            if (_currentPlayerLabel == null)
            {
                GameLog.Error("[GameplayMovesBinder] Bind failed: CurrentPlayerLabel is null.");

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.Assert(false, "GameplayMovesBinder.Bind() failed: CurrentPlayerLabel is null.");
#endif

                throw new InvalidOperationException("GameplayMovesBinder.Bind() failed: CurrentPlayerLabel is null.");
            }

            _subscriptions = new CompositeDisposable();
            try
            {
                _ui.CellClicks
                    .Subscribe(OnCellClicked)
                    .AddTo(_subscriptions);

                _moves.CellChanged
                    .Subscribe(OnCellChanged)
                    .AddTo(_subscriptions);

                _moves.LastMoveChanged
                    .Subscribe(OnLastMoveChanged)
                    .AddTo(_subscriptions);

                _moves.CurrentPlayer
                    .Subscribe(UpdateCurrentPlayerLabel)
                    .AddTo(_subscriptions);
            }
            catch
            {
                _subscriptions.Dispose();
                _subscriptions = null;
                _currentPlayerLabel = null;
                throw;
            }

            _isBound = true;

            RenderColdPathSnapshot();
            UpdateCurrentPlayerLabel(_moves.CurrentPlayer.CurrentValue);
        }

        public void Unbind()
        {
            if (!_isBound)
                return;

            _subscriptions?.Dispose();
            _subscriptions = null;
            _currentPlayerLabel = null;
            _isBound = false;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Unbind();
        }

        private void RenderColdPathSnapshot()
        {
            var cells = _moves.GetAllCells();
            for (var i = 0; i < cells.Count; i++)
                UpdateMark(cells[i].CellId, cells[i].Value);
        }

        private void OnCellClicked(CellId cellId)
        {
            if (!_isBound || _disposed)
                return;

            try
            {
                _ = _moves.TryApplyLocalClick(cellId);
            }
            catch (ObjectDisposedException)
            {
                // If scope is being torn down, ignore stray clicks.
            }
        }

        private void OnCellChanged(CellChangedEvent evt)
        {
            if (!_isBound || _disposed)
                return;

            UpdateMark(evt.CellId, evt.NewValue);
        }

        private void OnLastMoveChanged(LastMoveChangedEvent evt)
        {
            if (!_isBound || _disposed)
                return;

            if (evt.Previous != null)
                SetLastMoveClass(evt.Previous.Value, enabled: false);

            if (evt.Current != null)
                SetLastMoveClass(evt.Current.Value, enabled: true);
        }

        private void SetLastMoveClass(CellId cellId, bool enabled)
        {
            if (!_ui.TryGetCell(cellId, out var cellRoot) || cellRoot == null)
                return;

            if (enabled)
                cellRoot.AddToClassList(LastMoveClass);
            else
                cellRoot.RemoveFromClassList(LastMoveClass);
        }

        private void UpdateMark(CellId cellId, PlayerMark value)
        {
            if (!_ui.TryGetCellView(cellId, out var cellRoot, out var markLabel) || cellRoot == null || markLabel == null)
                return;

            ApplyCellInteractivity(cellRoot, value);

            markLabel.text = value.ToUiText();
        }

        private static void ApplyCellInteractivity(VisualElement cellRoot, PlayerMark value)
        {
            var occupied = value != PlayerMark.None;

            // ADR-6: disable occupied cells (no hover/pressed, clicks do not pass)
            cellRoot.SetEnabled(!occupied);
            cellRoot.pickingMode = occupied ? PickingMode.Ignore : PickingMode.Position;

            if (occupied)
                cellRoot.AddToClassList(DisabledClass);
            else
                cellRoot.RemoveFromClassList(DisabledClass);
        }

        private void UpdateCurrentPlayerLabel(PlayerMark mark)
        {
            if (_currentPlayerLabel == null)
                return;

            _currentPlayerLabel.text = mark.ToUiText();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(GameplayMovesBinder));
        }
    }
}
