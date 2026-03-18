using System;
using Runtime.Gameplay;
using Runtime.Infrastructure.Logging;
using StripLog;
using UnityEngine.UIElements;

namespace Runtime.Games.TicTacToe
{
    internal sealed class GameplayFieldPresenterCellFactory
    {
        private readonly GameplayFieldPresenterState _state;
        private readonly Action<CellId> _publishCellClick;
        private readonly Action<CellId> _publishOwnBoardCellClick;

        public GameplayFieldPresenterCellFactory(
            GameplayFieldPresenterState state,
            Action<CellId> publishCellClick,
            Action<CellId> publishOwnBoardCellClick)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _publishCellClick = publishCellClick ?? throw new ArgumentNullException(nameof(publishCellClick));
            _publishOwnBoardCellClick = publishOwnBoardCellClick ?? throw new ArgumentNullException(nameof(publishOwnBoardCellClick));
        }

        internal VisualElement CreateCell(int x, int y, CellId cellId)
        {
            var cell = new VisualElement { name = $"Cell_{x}_{y}" };
            cell.AddToClassList("cell");

            cell.userData = new CellUserData(cellId);
            cell.AddManipulator(new Clickable(() => _publishCellClick(cellId)));

            RegisterOpponentCell(cellId, cell);
            return cell;
        }

        internal VisualElement CreateOwnBoardCell(int x, int y, CellId cellId)
        {
            var cell = new VisualElement { name = $"OwnCell_{x}_{y}" };
            cell.AddToClassList("cell");
            cell.AddToClassList("cell--own-board");

            cell.userData = new CellUserData(cellId);
            cell.AddManipulator(new Clickable(() => _publishOwnBoardCellClick(cellId)));

            _state.OwnBoardCellById[cellId] = cell;

            var mark = new VisualElement { name = "OwnMark" };
            mark.AddToClassList("cell-mark");
            mark.AddToClassList("cell-mark--own-board");

            var markLabel = new Label { name = "OwnMarkLabel" };
            mark.Add(markLabel);
            cell.Add(mark);

            _state.OwnBoardMarkLabelById[cellId] = markLabel;

            return cell;
        }

        private void RegisterOpponentCell(CellId cellId, VisualElement cell)
        {
            try
            {
                _state.CellById.Add(cellId, cell);
            }
            catch (ArgumentException)
            {
                Log.Error(LogTags.UI, $"[GameplayFieldPresenter] Duplicate CellId detected: {cellId}");
                _state.CellById.Clear();
                _state.MarkById.Clear();
                _state.MarkLabelById.Clear();
                _state.IsCellIdCacheValid = false;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                throw new InvalidOperationException($"Duplicate CellId detected while building field: {cellId}");
#endif
            }

            var mark = new VisualElement { name = "Mark" };
            mark.AddToClassList("cell-mark");

            var markLabel = new Label { name = "MarkLabel" };
            mark.Add(markLabel);
            cell.Add(mark);

            if (!_state.IsCellIdCacheValid)
                return;

            try
            {
                _state.MarkById.Add(cellId, mark);
                _state.MarkLabelById.Add(cellId, markLabel);
            }
            catch (ArgumentException)
            {
                Log.Error(LogTags.UI, $"[GameplayFieldPresenter] Duplicate CellId detected (mark cache): {cellId}");
                _state.CellById.Clear();
                _state.MarkById.Clear();
                _state.MarkLabelById.Clear();
                _state.IsCellIdCacheValid = false;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                throw new InvalidOperationException($"Duplicate CellId detected while building field mark cache: {cellId}");
#endif
            }
        }
    }
}