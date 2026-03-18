using System;
using Runtime.Gameplay;
using UnityEngine;

namespace Runtime.Games.TicTacToe
{
    internal sealed class GameplayFieldPresenterCellLayoutSizer
    {
        private readonly GameplayFieldPresenterState _state;

        public GameplayFieldPresenterCellLayoutSizer(GameplayFieldPresenterState state)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
        }

        internal void UpdateCellSizes(Rect rect)
        {
            if (!TryResolveCellSize(rect, out var cellSize) || cellSize == _state.LastCellSize)
                return;

            _state.LastCellSize = cellSize;
            ResizeCells(cellSize);
            UpdateMarkFontSizes(cellSize);
            ResizeUltimateMiniBoards(cellSize);
        }

        internal void RefreshMiniBoardCenters()
        {
            if (_state.MiniBoardByMajor.Count == 0)
                return;

            foreach (var (key, mini) in _state.MiniBoardByMajor)
            {
                var rect = mini.worldBound;

                if (rect.width <= 0f || rect.height <= 0f)
                    continue;

                _state.MiniBoardCenterByMajor[key] = rect.center;
            }
        }

        private bool TryResolveCellSize(Rect rect, out int cellSize)
        {
            cellSize = 0;

            if (_state.Spec == null || _state.FieldContainer == null)
                return false;

            if (rect.width <= 0f || rect.height <= 0f)
                return false;

            if (_state.Spec.Kind == FieldKind.Ultimate)
            {
                cellSize = CalculateUltimateCellSize(rect, _state.Spec.OuterSize, _state.Spec.InnerSize);
                return cellSize > 0;
            }

            cellSize = _state.CurrentMode == GameplayFieldPresenterMode.BattleshipDual
                ? CalculateBattleshipDualCellSize(rect, _state.Spec.OuterSize)
                : CalculateClassicCellSize(rect, _state.Spec.OuterSize);

            return cellSize > 0;
        }

        private void ResizeCells(int cellSize)
        {
            foreach (var cell in _state.Cells)
            {
                cell.style.width = cellSize;
                cell.style.height = cellSize;
            }
        }

        private void ResizeUltimateMiniBoards(int cellSize)
        {
            if (_state.Spec?.Kind != FieldKind.Ultimate)
                return;

            var miniSize = cellSize * _state.Spec.InnerSize
                           + _state.Spec.InnerSize * (_state.GridGapHalf * 2f)
                           + _state.MiniBoardPadding * 2f
                           + _state.MiniBoardBorder * 2f;

            foreach (var mini in _state.MiniBoards)
            {
                mini.style.width = miniSize;
                mini.style.height = miniSize;
            }

            RefreshMiniBoardCenters();
        }

        private void UpdateMarkFontSizes(int cellSize)
        {
            if (cellSize <= 0)
                return;

            var scale = _state.MarkFontScale;

            if (scale <= 0f)
                scale = 0.62f;

            var fontSize = Mathf.Max(1f, cellSize * scale);

            foreach (var label in _state.MarkLabelById.Values)
            {
                if (label == null)
                    continue;

                label.style.fontSize = fontSize;
            }

            foreach (var label in _state.OwnBoardMarkLabelById.Values)
            {
                if (label == null)
                    continue;

                label.style.fontSize = fontSize;
            }
        }

        private int CalculateClassicCellSize(Rect rect, int size)
        {
            var totalMarginX = size * (_state.GridGapHalf * 2f);
            var totalMarginY = size * (_state.GridGapHalf * 2f);
            var availableWidth = rect.width - totalMarginX;
            var availableHeight = rect.height - totalMarginY;

            if (availableWidth <= 0f || availableHeight <= 0f)
                return 0;

            return Mathf.FloorToInt(Mathf.Min(availableWidth / size, availableHeight / size));
        }

        private int CalculateUltimateCellSize(Rect rect, int outer, int inner)
        {
            var totalMiniMarginX = outer * (_state.MiniBoardGapHalf * 2f);
            var totalMiniMarginY = outer * (_state.MiniBoardGapHalf * 2f);
            var availableWidth = rect.width - totalMiniMarginX;
            var availableHeight = rect.height - totalMiniMarginY;

            if (availableWidth <= 0f || availableHeight <= 0f)
                return 0;

            var miniAvailableWidth = availableWidth / outer;
            var miniAvailableHeight = availableHeight / outer;

            var innerMargins = inner * (_state.GridGapHalf * 2f);
            var fixedExtras = _state.MiniBoardPadding * 2f + _state.MiniBoardBorder * 2f + innerMargins;

            var cellAvailableWidth = miniAvailableWidth - fixedExtras;
            var cellAvailableHeight = miniAvailableHeight - fixedExtras;

            if (cellAvailableWidth <= 0f || cellAvailableHeight <= 0f)
                return 0;

            return Mathf.FloorToInt(Mathf.Min(cellAvailableWidth / inner, cellAvailableHeight / inner));
        }

        private int CalculateBattleshipDualCellSize(Rect rect, int size)
        {
            const float boardGap = 24f;
            const float boardTitleHeight = 26f;

            var totalMarginX = size * (_state.GridGapHalf * 2f);
            var totalMarginY = size * (_state.GridGapHalf * 2f);

            var perBoardAvailableWidth = (rect.width - boardGap) * 0.5f;
            var availableHeight = rect.height - boardTitleHeight;

            if (perBoardAvailableWidth <= 0f || availableHeight <= 0f)
                return 0;

            var cellAvailableWidth = perBoardAvailableWidth - totalMarginX;
            var cellAvailableHeight = availableHeight - totalMarginY;

            if (cellAvailableWidth <= 0f || cellAvailableHeight <= 0f)
                return 0;

            return Mathf.FloorToInt(Mathf.Min(cellAvailableWidth / size, cellAvailableHeight / size));
        }
    }
}