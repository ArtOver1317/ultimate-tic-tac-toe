using System;
using System.Collections.Generic;
using System.Diagnostics;
using R3;
using Runtime.Infrastructure.Logging;
using UnityEngine.UIElements;

namespace Runtime.Gameplay.Moves
{
    public sealed class GameplayMovesBinder : IDisposable
    {
        private const string _lastMoveClass = "cell--lastMove";
        private const string _disabledClass = "cell--disabled";
        private const string _markAppearFromClass = "cell-mark--appearFrom";
        private const string _markLabelXClass = "mark-label--x";
        private const string _markLabelOClass = "mark-label--o";

        private readonly IGameplayFieldUiAdapter _ui;
        private readonly ILocalMovesService _moves;
        private readonly MovesVfxSettings _vfxSettings;

        // Binder owns VFX state storage (do not use VisualElement.userData).
        private readonly Dictionary<CellId, MarkAppearVfxState> _markAppearVfxByCellId = new();

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

            _vfxSettings = NormalizeVfxSettings(MovesVfxSettings.Default);
        }

        public GameplayMovesBinder(
            IGameplayFieldUiAdapter ui,
            ILocalMovesService moves,
            MovesVfxSettings vfxSettings)
        {
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            _moves = moves ?? throw new ArgumentNullException(nameof(moves));

            _vfxSettings = NormalizeVfxSettings(vfxSettings);
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

            _currentPlayerLabel = AcquireCurrentPlayerLabel();

            _subscriptions = new CompositeDisposable();
            
            try
            {
                SubscribeToEvents();
            }
            catch
            {
                _subscriptions.Dispose();
                _subscriptions = null;
                _currentPlayerLabel = null;
                throw;
            }

            _isBound = true;

            var coldPathSnapshot = _moves.GetAllCells();

            SetupMarkAppearVfx(coldPathSnapshot);
            RenderColdPathSnapshot(coldPathSnapshot);
            UpdateCurrentPlayerLabel(_moves.CurrentPlayer.CurrentValue);
        }

        private Label AcquireCurrentPlayerLabel()
        {
            Label? label;
            
            try
            {
                label = _ui.CurrentPlayerLabel;
            }
            catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
            {
                GameLog.Error($"[GameplayMovesBinder] Bind failed: UI is not ready ({ex.GetType().Name}). {ex.Message}");

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.Assert(false, "GameplayMovesBinder.Bind() failed: UI is not ready.");
#endif

                throw new InvalidOperationException("GameplayMovesBinder.Bind() failed: UI is not ready.", ex);
            }

            if (label == null)
            {
                GameLog.Error("[GameplayMovesBinder] Bind failed: CurrentPlayerLabel is null.");

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.Assert(false, "GameplayMovesBinder.Bind() failed: CurrentPlayerLabel is null.");
#endif

                throw new InvalidOperationException("GameplayMovesBinder.Bind() failed: CurrentPlayerLabel is null.");
            }

            return label;
        }

        private void SubscribeToEvents()
        {
            _ui.CellClicks
                .Subscribe(OnCellClicked)
                .AddTo(_subscriptions!);

            _moves.CellChanged
                .Subscribe(OnCellChanged)
                .AddTo(_subscriptions!);

            _moves.LastMoveChanged
                .Subscribe(OnLastMoveChanged)
                .AddTo(_subscriptions!);

            _moves.CurrentPlayer
                .Skip(1)
                .Subscribe(UpdateCurrentPlayerLabel)
                .AddTo(_subscriptions!);
        }

        public void Unbind()
        {
            if (!_isBound)
                return;

            _subscriptions?.Dispose();
            _subscriptions = null;
            _currentPlayerLabel = null;

            foreach (var vfxState in _markAppearVfxByCellId.Values)
            {
                vfxState.CancelPending();
            }

            _markAppearVfxByCellId.Clear();
            _isBound = false;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Unbind();
        }

        private void RenderColdPathSnapshot(IReadOnlyList<CellValue> cells)
        {
            foreach (var cellValue in cells)
            {
                UpdateMark(cellValue.CellId, cellValue.Value, animate: false);
            }
        }

        private void SetupMarkAppearVfx(IReadOnlyList<CellValue> cells)
        {
            _markAppearVfxByCellId.Clear();

            if (!_vfxSettings.EnableMarkAppearAnimation)
                return;

            var durationSeconds = _vfxSettings.MarkAppearDurationSeconds;
            
            if (durationSeconds <= 0f)
                return;

            var duration = new TimeValue(durationSeconds, TimeUnit.Second);

            foreach (var cell in cells)
            {
                var cellId = cell.CellId;

                if (!_ui.TryGetMark(cellId, out var markRoot) || markRoot == null)
                    continue;

                // Make settings "alive": override transition duration once (hot path stays allocation-free).
                // Avoid sharing mutable collections between elements.
                markRoot.style.transitionDuration = new List<TimeValue>(capacity: 1) { duration };

                // Pre-create scheduled item and cached delegate once per cell to avoid per-move GC alloc.
                _markAppearVfxByCellId[cellId] = new MarkAppearVfxState(markRoot);
            }
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

            UpdateMark(evt.CellId, evt.NewValue, animate: true);
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
                cellRoot.AddToClassList(_lastMoveClass);
            else
                cellRoot.RemoveFromClassList(_lastMoveClass);
        }

        private void UpdateMark(CellId cellId, PlayerMark value, bool animate)
        {
            if (!_ui.TryGetCellView(cellId, out var cellRoot, out var markLabel) || cellRoot == null || markLabel == null)
                return;

            var wasEmpty = string.IsNullOrEmpty(markLabel.text);

            ApplyCellInteractivity(cellRoot, value);

            markLabel.text = value.ToUiText();
            ApplyMarkLabelClass(markLabel, value);

            if (!_ui.TryGetMark(cellId, out var markRoot) || markRoot == null)
                return;

            // Ensure no leftover animation state when clearing.
            if (value == PlayerMark.None)
            {
                markRoot.RemoveFromClassList(_markAppearFromClass);

                if (_markAppearVfxByCellId.TryGetValue(cellId, out var state))
                    state.CancelPending();
                
                return;
            }

            if (!animate)
                return;

            if (!_vfxSettings.EnableMarkAppearAnimation)
                return;

            // Animate only on a new mark appearing (not on cold-path render).
            if (!wasEmpty)
                return;

            if (_markAppearVfxByCellId.TryGetValue(cellId, out var vfxState))
                vfxState.Trigger();
        }

        private static void ApplyMarkLabelClass(Label markLabel, PlayerMark value)
        {
            // Reset first to avoid class build-up on reuse.
            markLabel.RemoveFromClassList(_markLabelXClass);
            markLabel.RemoveFromClassList(_markLabelOClass);

            if (value == PlayerMark.X)
                markLabel.AddToClassList(_markLabelXClass);
            else if (value == PlayerMark.O)
                markLabel.AddToClassList(_markLabelOClass);
        }

        private static MovesVfxSettings NormalizeVfxSettings(MovesVfxSettings settings)
        {
            if (!settings.EnableMarkAppearAnimation || settings.MarkAppearDurationSeconds <= 0f)
                return new MovesVfxSettings(enableMarkAppearAnimation: false, markAppearDurationSeconds: 0f);

            return settings;
        }

        private sealed class MarkAppearVfxState
        {
            private readonly VisualElement _markRoot;
            private readonly IVisualElementScheduledItem _removeAppearFromItem;

            public MarkAppearVfxState(VisualElement markRoot)
            {
                _markRoot = markRoot;

                // Cache scheduled item + delegate once. We keep it paused and re-arm on demand.
                _removeAppearFromItem = _markRoot.schedule.Execute(RemoveAppearFromClass);
                _removeAppearFromItem.Pause();
            }

            public void Trigger()
            {
                _markRoot.RemoveFromClassList(_markAppearFromClass);
                _markRoot.AddToClassList(_markAppearFromClass);

                // Remove on next update so transition reliably animates to the base state.
                _removeAppearFromItem.Pause();
                _removeAppearFromItem.StartingIn(1);
                _removeAppearFromItem.Resume();
            }

            public void CancelPending() => _removeAppearFromItem.Pause();

            private void RemoveAppearFromClass() => _markRoot.RemoveFromClassList(_markAppearFromClass);
        }

        private static void ApplyCellInteractivity(VisualElement cellRoot, PlayerMark value)
        {
            var occupied = value != PlayerMark.None;

            // ADR-6: disable occupied cells (no hover/pressed, clicks do not pass)
            cellRoot.SetEnabled(!occupied);
            cellRoot.pickingMode = occupied ? PickingMode.Ignore : PickingMode.Position;

            if (occupied)
                cellRoot.AddToClassList(_disabledClass);
            else
                cellRoot.RemoveFromClassList(_disabledClass);
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
