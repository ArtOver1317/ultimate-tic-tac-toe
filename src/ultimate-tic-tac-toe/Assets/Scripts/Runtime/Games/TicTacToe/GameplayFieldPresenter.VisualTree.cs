using System;
using System.Collections.Generic;
using System.Diagnostics;
using Runtime.Games.TicTacToe.Moves;
using Runtime.Infrastructure.Logging;
using StripLog;
using UnityEngine.UIElements;

using Runtime.Gameplay;
namespace Runtime.Games.TicTacToe
{
    /// <summary>
    /// Visual tree construction: builds Classic/Ultimate grids, creates cells and mark elements.
    /// </summary>
    public sealed partial class GameplayFieldPresenter
    {
        private void BuildVisualTree()
        {
            var stopwatch = Stopwatch.StartNew();

            _root = _uiDocument.rootVisualElement ?? throw new InvalidOperationException("UIDocument root is null.");
            var fieldRoot = _root.Q<VisualElement>("GameplayFieldRoot");

            if (fieldRoot == null)
            {
                fieldRoot = new VisualElement { name = "GameplayFieldRoot" };
                fieldRoot.AddToClassList("gameplay-field-root");
                _root.Add(fieldRoot);
            }

            fieldRoot.AddToClassList("gameplay-field-root");

            _fieldRoot = fieldRoot;
            EnsureCustomStyleCallbackRegistered(_fieldRoot);

            UpdateSpacingFromCustomStyle(_fieldRoot.customStyle, validate: false);

            _fieldContainer = _fieldRoot.Q<VisualElement>("FieldContainer");
            
            if (_fieldContainer == null)
            {
                _fieldContainer = new VisualElement { name = "FieldContainer" };
                _fieldContainer.AddToClassList("field-container");
                _fieldRoot.Add(_fieldContainer);
            }

            _backButton = _fieldRoot.Q<Button>("BackButton");
            
            if (_backButton != null)
                _backButton.clicked += OnBackClicked;
            else
                Log.Error(LogTags.UI, "[GameplayFieldPresenter] BackButton not found.");

            EnsureCurrentPlayerLabelExists();

            _fieldContainer.Clear();
            _fieldContainer.RemoveFromClassList("field-container--classic");
            _fieldContainer.RemoveFromClassList("field-container--ultimate");

            _cellById.Clear();
            _markById.Clear();
            _markLabelById.Clear();
            _isCellIdCacheValid = true;

            if (_spec.Kind == FieldKind.Classic)
            {
                _fieldContainer.AddToClassList("field-container--classic");
                BuildClassic(_spec);
            }
            else
            {
                _fieldContainer.AddToClassList("field-container--ultimate");
                BuildUltimate(_spec);
            }

            _fieldContainer.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);

            stopwatch.Stop();
            Log.Info(LogTags.UI, $"[GameplayFieldPresenter] Field build: {_spec.Kind}, {stopwatch.ElapsedMilliseconds} ms");
        }

        private void EnsureCurrentPlayerLabelExists()
        {
            if (_fieldRoot == null)
                return;

            var existing = _fieldRoot.Q<Label>("CurrentPlayerLabel");
            
            if (existing != null)
            {
                _currentPlayerLabel = existing;
                return;
            }

            var toolbar = _fieldRoot.Q<VisualElement>("GameplayToolbar") ?? _fieldRoot;

            var label = new Label
            {
                name = "CurrentPlayerLabel",
                text = string.Empty,
            };
            
            label.AddToClassList("current-player-label");

            toolbar.Add(label);
            _currentPlayerLabel = label;
        }

        internal bool TryGetCell(CellId id, out VisualElement cellRoot)
        {
            if (!_isBound || _disposed || _spec == null || !_isCellIdCacheValid)
            {
                cellRoot = null;
                return false;
            }

            return _cellById.TryGetValue(id, out cellRoot);
        }

        internal bool TryGetMark(CellId id, out VisualElement mark)
        {
            if (!_isBound || _disposed || _spec == null || !_isCellIdCacheValid)
            {
                mark = null;
                return false;
            }

            return _markById.TryGetValue(id, out mark);
        }

        private void EnsureCustomStyleCallbackRegistered(VisualElement fieldRoot)
        {
            if (fieldRoot == null)
                return;

            if (ReferenceEquals(_customStyleCallbackElement, fieldRoot))
                return;

            _customStyleCallbackElement?.UnregisterCallback<CustomStyleResolvedEvent>(OnCustomStyleResolved);

            _customStyleCallbackElement = fieldRoot;
            _customStyleCallbackElement.RegisterCallback<CustomStyleResolvedEvent>(OnCustomStyleResolved);
        }

        private void BuildClassic(FieldRenderSpec spec)
        {
            var size = spec.OuterSize;
            
            for (var y = 0; y < size; y++)
            {
                var row = new VisualElement();
                row.AddToClassList("field-row");

                for (var x = 0; x < size; x++)
                {
                    var cellId = new CellId(x, y);
                    var cell = CreateCell(x, y, cellId);
                    row.Add(cell);
                    _cells.Add(cell);
                }

                _fieldContainer.Add(row);
            }
        }

        private void BuildUltimate(FieldRenderSpec spec)
        {
            var outer = spec.OuterSize;
            var inner = spec.InnerSize;

            for (var miniY = 0; miniY < outer; miniY++)
            {
                var miniRow = new VisualElement();
                miniRow.AddToClassList("mini-row");

                for (var miniX = 0; miniX < outer; miniX++)
                {
                    var mini = new VisualElement { name = $"Mini_{miniX}_{miniY}" };
                    mini.AddToClassList("mini-board");

                    var miniIndex = miniY * outer + miniX;

                    for (var y = 0; y < inner; y++)
                    {
                        var row = new VisualElement();
                        row.AddToClassList("mini-row-inner");

                        for (var x = 0; x < inner; x++)
                        {
                            var minor = y * inner + x;
                            var cellId = new CellId(miniIndex, minor);
                            var cell = CreateCell(x, y, cellId);
                            row.Add(cell);
                            _cells.Add(cell);
                        }

                        mini.Add(row);
                    }

                    miniRow.Add(mini);
                    _miniBoards.Add(mini);
                }

                _fieldContainer.Add(miniRow);
            }
        }

        private VisualElement CreateCell(int x, int y, CellId cellId)
        {
            var cell = new VisualElement { name = $"Cell_{x}_{y}" };
            cell.AddToClassList("cell");

            cell.userData = new CellUserData(cellId);

            cell.AddManipulator(new Clickable(() => OnCellClicked(cell)));
            
            try
            {
                _cellById.Add(cellId, cell);
            }
            catch (ArgumentException)
            {
                Log.Error(LogTags.UI, $"[GameplayFieldPresenter] Duplicate CellId detected: {cellId}");

                // Don't keep partially broken cache state.
                _cellById.Clear();
                _markById.Clear();
                _markLabelById.Clear();
                _isCellIdCacheValid = false;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                throw new InvalidOperationException($"Duplicate CellId detected while building field: {cellId}");
#endif
            }

            var mark = new VisualElement { name = "Mark" };
            mark.AddToClassList("cell-mark");

            var markLabel = new Label { name = "MarkLabel" };
            mark.Add(markLabel);
            cell.Add(mark);

            if (_isCellIdCacheValid)
            {
                try
                {
                    _markById.Add(cellId, mark);
                    _markLabelById.Add(cellId, markLabel);
                }
                catch (ArgumentException)
                {
                    Log.Error(LogTags.UI, $"[GameplayFieldPresenter] Duplicate CellId detected (mark cache): {cellId}");

                    _cellById.Clear();
                    _markById.Clear();
                    _markLabelById.Clear();
                    _isCellIdCacheValid = false;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    throw new InvalidOperationException($"Duplicate CellId detected while building field mark cache: {cellId}");
#endif
                }
            }

            return cell;
        }

        internal void EmitCellClick(CellId cellId)
        {
            if (!_isBound || _disposed)
                return;

            _cellClicks.OnNext(cellId);
        }

        internal void OnCellClicked(VisualElement cell)
        {
            if (!_isBound || _disposed || cell?.userData is not CellUserData userData)
                return;

            EmitCellClick(userData.CellId);
        }
    }
}
