using System;
using System.Collections.Generic;
using System.Diagnostics;
using R3;
using Runtime.GameModes.Wizard;
using Runtime.GameModes.Wizard.Online;
using Runtime.Gameplay;
using Runtime.Gameplay.ECS;
using Runtime.Games.Battleship;
using Runtime.Games.TicTacToe.ECS;
using Runtime.Infrastructure.Logging;
using UnityEngine.UIElements;

namespace Runtime.Games.TicTacToe.Moves
{
    public sealed class GameplayMovesBinder : IDisposable
    {
        private const string _lastMoveClass = "cell--lastMove";
        private const string _disabledClass = "cell--disabled";
        private const string _markAppearFromClass = "cell-mark--appearFrom";
        private const string _markLabelXClass = "mark-label--x";
        private const string _markLabelOClass = "mark-label--o";

        private readonly IGameplayFieldUiAdapter _ui;
        private readonly IGameplayCommandSink _commandSink;
        private readonly IGameplayEventStream _eventStream;
        private readonly IGameplaySnapshotProvider _snapshotProvider;
        private readonly MovesVfxSettings _vfxSettings;
        private readonly IOnlineGameplaySessionContextStore _sessionContextStore;

        // Binder owns VFX state storage (do not use VisualElement.userData).
        private readonly Dictionary<CellId, MarkAppearVfxState> _markAppearVfxByCellId = new();

        private CompositeDisposable _subscriptions;
        private Label _currentPlayerLabel;
        private CellId? _lastMoveHighlightCell;
        private bool _isBound;
        private bool _disposed;

        public GameplayMovesBinder(
            IGameplayFieldUiAdapter ui,
            IGameplayCommandSink commandSink,
            IGameplayEventStream eventStream,
            IGameplaySnapshotProvider snapshotProvider,
            IOnlineGameplaySessionContextStore sessionContextStore = null)
        {
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            _commandSink = commandSink ?? throw new ArgumentNullException(nameof(commandSink));
            _eventStream = eventStream ?? throw new ArgumentNullException(nameof(eventStream));
            _snapshotProvider = snapshotProvider ?? throw new ArgumentNullException(nameof(snapshotProvider));
            _vfxSettings = NormalizeVfxSettings(MovesVfxSettings.Default);
            _sessionContextStore = sessionContextStore ?? new OnlineGameplaySessionContextStore();
        }

        public GameplayMovesBinder(
            IGameplayFieldUiAdapter ui,
            IGameplayCommandSink commandSink,
            IGameplayEventStream eventStream,
            IGameplaySnapshotProvider snapshotProvider,
            MovesVfxSettings vfxSettings,
            IOnlineGameplaySessionContextStore sessionContextStore = null)
        {
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            _commandSink = commandSink ?? throw new ArgumentNullException(nameof(commandSink));
            _eventStream = eventStream ?? throw new ArgumentNullException(nameof(eventStream));
            _snapshotProvider = snapshotProvider ?? throw new ArgumentNullException(nameof(snapshotProvider));
            _vfxSettings = NormalizeVfxSettings(vfxSettings);
            _sessionContextStore = sessionContextStore ?? new OnlineGameplaySessionContextStore();
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

            // ECS board is initialized at StartMatch — no "IsStarted" check needed.

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

            var ecsCells = _snapshotProvider.GetAllCells();
            var coldPathSnapshot = MapEcsCells(ecsCells);
            var isBattleshipMode = IsBattleshipMode();

            if (isBattleshipMode)
                RefreshBattleshipOpponentInteractivity();
            else
            {
                SetupMarkAppearVfx(coldPathSnapshot);
                RenderColdPathSnapshot(coldPathSnapshot);
            }
            // Show the correct starting player from ECS snapshot (handles restarts with O as starter)
            UpdateCurrentPlayerLabel(PlayerSlotMapping.SlotToMark(_snapshotProvider.ActivePlayerSlot));
        }

        private Label AcquireCurrentPlayerLabel()
        {
            Label label;
            
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

            _eventStream.CellChanged
                .Subscribe(OnEcsCellChanged)
                .AddTo(_subscriptions!);

            _eventStream.LastMoveChanged
                .Subscribe(OnEcsLastMoveChanged)
                .AddTo(_subscriptions!);

            _eventStream.CurrentPlayerChanged
                .Subscribe(evt => UpdateCurrentPlayerLabel(PlayerSlotMapping.SlotToMark(evt.ActivePlayerSlot)))
                .AddTo(_subscriptions!);

            _eventStream.CommandRejected
                .Subscribe(OnCommandRejected)
                .AddTo(_subscriptions!);
        }

        public void Unbind()
        {
            if (!_isBound)
                return;

            _subscriptions?.Dispose();
            _subscriptions = null;
            _currentPlayerLabel = null;

            // Bug fix: remove stale cell--lastMove highlight so it doesn't persist across rounds.
            if (_lastMoveHighlightCell != null)
            {
                SetLastMoveClass(_lastMoveHighlightCell.Value, enabled: false);
                _lastMoveHighlightCell = null;
            }

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

        private void RenderColdPathInteractivitySnapshot(IReadOnlyList<CellValue> cells)
        {
            foreach (var cellValue in cells)
            {
                if (!_ui.TryGetCell(cellValue.CellId, out var cellRoot) || cellRoot == null)
                    continue;

                ApplyCellInteractivity(cellRoot, cellValue.Value);
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

            if (TryGetBattleshipSnapshot(out var battleshipSnapshot)
                && battleshipSnapshot.Phase != BattleshipPhase.Battle)
            {
                return;
            }

            try
            {
                _commandSink.SubmitCommand(new MakeMoveCommand(cellId));
            }
            catch (ObjectDisposedException)
            {
                // If scope is being torn down, ignore stray clicks.
            }
        }

        private void OnCommandRejected(CommandRejectedEvent evt)
        {
            if (!_isBound || _disposed)
                return;

            // Log rejection for debugging. Full UX feedback (toast/shake) can be added later.
            GameLog.Warning($"[GameplayMovesBinder] Command rejected: {evt.Rejection.Reason}");
        }

        private void OnEcsCellChanged(CellChangedEvent evt)
        {
            if (!_isBound || _disposed)
                return;

            if (IsBattleshipMode())
            {
                // Battleship has two 10x10 boards with shared CellId coordinates.
                // Generic CellChanged/CellSnapshot events are board-agnostic, so use viewer-relative
                // opponent marks instead of raw events to avoid cross-board interactivity bleed.
                RefreshBattleshipOpponentInteractivity();

                return;
            }

            UpdateMark(evt.CellId, PlayerSlotMapping.SlotToMark(evt.NewSlot), animate: true);
        }

        private bool IsBattleshipMode() =>
            TryGetBattleshipSnapshot(out _);

        private bool TryGetBattleshipSnapshot(out IBattleshipGameplaySnapshotProvider snapshot)
        {
            snapshot = null;

            if (_snapshotProvider is not IBattleshipGameplaySnapshotProvider battleshipSnapshot)
                return false;

            if (!battleshipSnapshot.TryGetConsecutiveTimeouts(out _, out _))
                return false;

            snapshot = battleshipSnapshot;
            return true;
        }

        private void OnEcsLastMoveChanged(LastMoveChangedEvent evt)
        {
            if (!_isBound || _disposed)
                return;

            if (IsBattleshipMode())
            {
                // LastMoveChanged has no board context (own/opponent), so highlighting may end up
                // on the wrong board in Battleship dual-board UI. Skip generic highlight here.
                if (_lastMoveHighlightCell != null)
                {
                    SetLastMoveClass(_lastMoveHighlightCell.Value, enabled: false);
                    _lastMoveHighlightCell = null;
                }

                return;
            }

            // Track previous highlight locally (ECS event only has current CellId)
            if (_lastMoveHighlightCell != null)
                SetLastMoveClass(_lastMoveHighlightCell.Value, enabled: false);

            if (evt.CellId != null)
                SetLastMoveClass(evt.CellId.Value, enabled: true);

            _lastMoveHighlightCell = evt.CellId;
        }

        private static IReadOnlyList<CellValue> MapEcsCells(IReadOnlyList<CellSnapshot> ecsCells)
        {
            var result = new CellValue[ecsCells.Count];
            for (var i = 0; i < ecsCells.Count; i++)
                result[i] = new CellValue(ecsCells[i].CellId, PlayerSlotMapping.SlotToMark(ecsCells[i].Slot));
            return result;
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

            public void CancelPending()
            {
                _removeAppearFromItem.Pause();
                // Bug fix: remove animation class so the mark doesn't stay at opacity:0
                // when Unbind() cancels a pending appear animation (e.g. on winning move).
                _markRoot.RemoveFromClassList(_markAppearFromClass);
            }

            private void RemoveAppearFromClass() => _markRoot.RemoveFromClassList(_markAppearFromClass);
        }

        private static void ApplyCellInteractivity(VisualElement cellRoot, PlayerMark value)
        {
            var occupied = value != PlayerMark.None;
            ApplyCellInteractivity(cellRoot, occupied);
        }

        private static void ApplyCellInteractivity(VisualElement cellRoot, bool occupied)
        {
            if (cellRoot == null)
                return;

            // ADR-6: disable occupied cells (no hover/pressed, clicks do not pass)
            cellRoot.SetEnabled(!occupied);
            cellRoot.pickingMode = occupied ? PickingMode.Ignore : PickingMode.Position;

            if (occupied)
                cellRoot.AddToClassList(_disabledClass);
            else
                cellRoot.RemoveFromClassList(_disabledClass);
        }

        private void RefreshBattleshipOpponentInteractivity()
        {
            if (!TryGetBattleshipSnapshot(out var snapshot))
                return;

            var localSlot = ResolveLocalSlot();
            var marks = snapshot.GetOpponentMarks(localSlot);
            if (marks == null || marks.Count == 0)
                return;

            var boardSize = ResolveBoardSize(marks.Count);
            var count = marks.Count;
            for (var index = 0; index < count; index++)
            {
                var row = index / boardSize;
                var col = index % boardSize;
                var cellId = new CellId(row, col);

                if (!_ui.TryGetCell(cellId, out var cellRoot) || cellRoot == null)
                    continue;

                var occupied = marks[index] != BattleshipCellMark.Unknown;
                ApplyCellInteractivity(cellRoot, occupied);
            }
        }

        private int ResolveLocalSlot()
        {
            var session = _sessionContextStore.Snapshot;
            if (!session.IsOnlineDirectInvite)
                return PlayerSlotMapping.SlotX;

            return session.IsHost
                ? PlayerSlotMapping.SlotX
                : PlayerSlotMapping.SlotO;
        }

        private static int ResolveBoardSize(int cellCount)
        {
            if (cellCount <= 0)
                return 10;

            var root = (int)Math.Sqrt(cellCount);
            return root > 0 && root * root == cellCount
                ? root
                : 10;
        }

        private void UpdateCurrentPlayerLabel(PlayerMark mark)
        {
            if (_currentPlayerLabel == null)
                return;

            _currentPlayerLabel.text = mark.ToTurnIndicatorText();

            UpdateActivePlayerPanel(mark);
        }

        private void UpdateActivePlayerPanel(PlayerMark mark)
        {
            const string activeClass = "player-panel--active";

            var p1 = _ui.Player1Panel;
            var p2 = _ui.Player2Panel;
            if (p1 == null || p2 == null) return;

            if (mark == PlayerMark.X)
            {
                p1.AddToClassList(activeClass);
                p2.RemoveFromClassList(activeClass);
            }
            else if (mark == PlayerMark.O)
            {
                p1.RemoveFromClassList(activeClass);
                p2.AddToClassList(activeClass);
            }
            else
            {
                p1.RemoveFromClassList(activeClass);
                p2.RemoveFromClassList(activeClass);
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(GameplayMovesBinder));
        }
    }
}
