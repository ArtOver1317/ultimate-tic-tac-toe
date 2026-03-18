using System.Collections.Generic;
using Runtime.Gameplay.Shared;
using UnityEngine.UIElements;

namespace Runtime.Gameplay
{
    public interface IGameplayMovesModeBehavior
    {
        void Initialize(GameplayMovesFieldRenderer renderer, IReadOnlyList<CellValue> cells);
        bool CanSubmitCellClick();
        void HandleCellChanged(GameplayMovesFieldRenderer renderer, CellChangedEvent evt);
        void HandleLastMoveChanged(GameplayMovesFieldRenderer renderer, LastMoveChangedEvent evt);
    }

    internal sealed class DefaultGameplayMovesModeBehavior : IGameplayMovesModeBehavior
    {
        public static DefaultGameplayMovesModeBehavior Instance { get; } = new();

        private DefaultGameplayMovesModeBehavior() { }

        public void Initialize(GameplayMovesFieldRenderer renderer, IReadOnlyList<CellValue> cells)
        {
            renderer.Prepare(cells);
            renderer.RenderSnapshot(cells);
        }

        public bool CanSubmitCellClick() => true;

        public void HandleCellChanged(GameplayMovesFieldRenderer renderer, CellChangedEvent evt) =>
            renderer.UpdateMark(evt.CellId, PlayerSlotMapping.SlotToMark(evt.NewSlot), animate: true);

        public void HandleLastMoveChanged(GameplayMovesFieldRenderer renderer, LastMoveChangedEvent evt) =>
            renderer.UpdateLastMove(evt.CellId);
    }

    public sealed class GameplayMovesFieldRenderer
    {
        private const string LastMoveClass = "cell--lastMove";
        private const string DisabledClass = "cell--disabled";
        private const string MarkAppearFromClass = "cell-mark--appearFrom";
        private const string MarkLabelXClass = "mark-label--x";
        private const string MarkLabelOClass = "mark-label--o";

        private readonly IGameplayFieldUiAdapter _ui;
        private readonly MovesVfxSettings _vfxSettings;
        private readonly Dictionary<CellId, MarkAppearVfxState> _markAppearVfxByCellId = new();

        private CellId? _lastMoveHighlightCell;

        public GameplayMovesFieldRenderer(IGameplayFieldUiAdapter ui, MovesVfxSettings vfxSettings)
        {
            _ui = ui;
            _vfxSettings = vfxSettings;
        }

        public void Prepare(IReadOnlyList<CellValue> cells)
        {
            Reset();

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

                markRoot.style.transitionDuration = new List<TimeValue>(capacity: 1) { duration };
                _markAppearVfxByCellId[cellId] = new MarkAppearVfxState(markRoot);
            }
        }

        public void Reset()
        {
            ClearLastMoveHighlight();

            foreach (var vfxState in _markAppearVfxByCellId.Values)
            {
                vfxState.CancelPending();
            }

            _markAppearVfxByCellId.Clear();
        }

        public void RenderSnapshot(IReadOnlyList<CellValue> cells)
        {
            foreach (var cellValue in cells)
            {
                UpdateMark(cellValue.CellId, cellValue.Value, animate: false);
            }
        }

        public void UpdateMark(CellId cellId, PlayerMark value, bool animate)
        {
            if (!_ui.TryGetCellView(cellId, out var cellRoot, out var markLabel) || cellRoot == null || markLabel == null)
                return;

            var wasEmpty = string.IsNullOrEmpty(markLabel.text);

            ApplyCellInteractivity(cellRoot, value != PlayerMark.None);

            markLabel.text = value.ToUiText();
            ApplyMarkLabelClass(markLabel, value);

            if (!_ui.TryGetMark(cellId, out var markRoot) || markRoot == null)
                return;

            if (value == PlayerMark.None)
            {
                markRoot.RemoveFromClassList(MarkAppearFromClass);

                if (_markAppearVfxByCellId.TryGetValue(cellId, out var state))
                    state.CancelPending();

                return;
            }

            if (!animate || !_vfxSettings.EnableMarkAppearAnimation || !wasEmpty)
                return;

            if (_markAppearVfxByCellId.TryGetValue(cellId, out var vfxState))
                vfxState.Trigger();
        }

        public void UpdateLastMove(CellId? cellId)
        {
            if (_lastMoveHighlightCell != null)
                SetLastMoveClass(_lastMoveHighlightCell.Value, enabled: false);

            if (cellId != null)
                SetLastMoveClass(cellId.Value, enabled: true);

            _lastMoveHighlightCell = cellId;
        }

        public void ClearLastMoveHighlight()
        {
            if (_lastMoveHighlightCell == null)
                return;

            SetLastMoveClass(_lastMoveHighlightCell.Value, enabled: false);
            _lastMoveHighlightCell = null;
        }

        public void UpdateCellInteractivity(CellId cellId, bool occupied)
        {
            if (!_ui.TryGetCell(cellId, out var cellRoot) || cellRoot == null)
                return;

            ApplyCellInteractivity(cellRoot, occupied);
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

        private static void ApplyMarkLabelClass(Label markLabel, PlayerMark value)
        {
            markLabel.RemoveFromClassList(MarkLabelXClass);
            markLabel.RemoveFromClassList(MarkLabelOClass);

            if (value == PlayerMark.X)
                markLabel.AddToClassList(MarkLabelXClass);
            else if (value == PlayerMark.O)
                markLabel.AddToClassList(MarkLabelOClass);
        }

        private static void ApplyCellInteractivity(VisualElement cellRoot, bool occupied)
        {
            if (cellRoot == null)
                return;

            cellRoot.SetEnabled(!occupied);
            cellRoot.pickingMode = occupied ? PickingMode.Ignore : PickingMode.Position;

            if (occupied)
                cellRoot.AddToClassList(DisabledClass);
            else
                cellRoot.RemoveFromClassList(DisabledClass);
        }

        private sealed class MarkAppearVfxState
        {
            private readonly VisualElement _markRoot;
            private readonly IVisualElementScheduledItem _removeAppearFromItem;

            public MarkAppearVfxState(VisualElement markRoot)
            {
                _markRoot = markRoot;
                _removeAppearFromItem = _markRoot.schedule.Execute(RemoveAppearFromClass);
                _removeAppearFromItem.Pause();
            }

            public void Trigger()
            {
                _markRoot.RemoveFromClassList(MarkAppearFromClass);
                _markRoot.AddToClassList(MarkAppearFromClass);

                _removeAppearFromItem.Pause();
                _removeAppearFromItem.StartingIn(1);
                _removeAppearFromItem.Resume();
            }

            public void CancelPending()
            {
                _removeAppearFromItem.Pause();
                _markRoot.RemoveFromClassList(MarkAppearFromClass);
            }

            private void RemoveAppearFromClass() => _markRoot.RemoveFromClassList(MarkAppearFromClass);
        }
    }
}