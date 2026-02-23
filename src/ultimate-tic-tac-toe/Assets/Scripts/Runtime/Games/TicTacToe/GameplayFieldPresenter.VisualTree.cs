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
            _miniBoardByMajor.Clear();
            _miniBoardCenterByMajor.Clear();
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

            // Build full scoreboard (replaces the old single-label turn indicator).
            BuildScoreboard();
        }

        /// <summary>
        /// Builds a centered scoreboard above the field:
        ///   [Player 1 (X) | score] — vs — [Player 2 (O) | score]
        /// Active player's panel gets "player-panel--active" class.
        /// </summary>
        private void BuildScoreboard()
        {
            var existing = _fieldRoot.Q<VisualElement>("Scoreboard");
            if (existing != null)
            {
                // Reuse existing DOM — just re-acquire references.
                AcquireScoreboardReferences(existing);
                return;
            }

            var scoreboard = new VisualElement { name = "Scoreboard" };
            scoreboard.AddToClassList("scoreboard");

            // Player 1 panel
            var p1Panel = new VisualElement { name = "Player1Panel" };
            p1Panel.AddToClassList("player-panel");

            var p1Name = new Label { name = "Player1Name", text = "Player 1 (X)" };
            p1Name.AddToClassList("player-name");
            p1Panel.Add(p1Name);

            var p1Score = new Label { name = "Player1Score", text = "0" };
            p1Score.AddToClassList("player-score");
            p1Panel.Add(p1Score);

            scoreboard.Add(p1Panel);

            // Center divider / turn label
            var centerLabel = new Label { name = "CurrentPlayerLabel", text = string.Empty };
            centerLabel.AddToClassList("current-player-label");
            scoreboard.Add(centerLabel);

            var moveTimerLabel = new Label { name = "MoveTimerLabel", text = "00" };
            moveTimerLabel.AddToClassList("player-score");
            moveTimerLabel.AddToClassList("move-timer-label");
            moveTimerLabel.style.display = DisplayStyle.None;
            scoreboard.Add(moveTimerLabel);

            var drawsScore = new Label { name = "DrawsScore", text = "D:0" };
            drawsScore.AddToClassList("player-score");
            scoreboard.Add(drawsScore);

            // Player 2 panel
            var p2Panel = new VisualElement { name = "Player2Panel" };
            p2Panel.AddToClassList("player-panel");

            var p2Name = new Label { name = "Player2Name", text = "Player 2 (O)" };
            p2Name.AddToClassList("player-name");
            p2Panel.Add(p2Name);

            var p2Score = new Label { name = "Player2Score", text = "0" };
            p2Score.AddToClassList("player-score");
            p2Panel.Add(p2Score);

            scoreboard.Add(p2Panel);

            // Insert scoreboard between toolbar and field container.
            var toolbar = _fieldRoot.Q<VisualElement>("GameplayToolbar");
            if (toolbar != null)
            {
                var idx = _fieldRoot.IndexOf(toolbar);
                _fieldRoot.Insert(idx + 1, scoreboard);
            }
            else
            {
                _fieldRoot.Insert(0, scoreboard);
            }

            AcquireScoreboardReferences(scoreboard);
        }

        private void AcquireScoreboardReferences(VisualElement scoreboard)
        {
            _player1Panel = scoreboard.Q<VisualElement>("Player1Panel");
            _player2Panel = scoreboard.Q<VisualElement>("Player2Panel");
            _player1ScoreLabel = scoreboard.Q<Label>("Player1Score");
            _player2ScoreLabel = scoreboard.Q<Label>("Player2Score");
            _drawsScoreLabel = scoreboard.Q<Label>("DrawsScore");
            _moveTimerLabel = scoreboard.Q<Label>("MoveTimerLabel");

            if (_moveTimerLabel == null)
            {
                _moveTimerLabel = new Label { name = "MoveTimerLabel", text = "00" };
                _moveTimerLabel.AddToClassList("player-score");
                _moveTimerLabel.AddToClassList("move-timer-label");
                _moveTimerLabel.style.display = DisplayStyle.None;

                var currentPlayerLabel = scoreboard.Q<Label>("CurrentPlayerLabel");
                if (currentPlayerLabel != null)
                {
                    var index = scoreboard.IndexOf(currentPlayerLabel);
                    scoreboard.Insert(index + 1, _moveTimerLabel);
                }
                else
                {
                    scoreboard.Insert(0, _moveTimerLabel);
                }
            }

            _currentPlayerLabel = scoreboard.Q<Label>("CurrentPlayerLabel");
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

                    var miniStatusOverlay = new Label { name = "MiniStatusOverlay", text = string.Empty };
                    miniStatusOverlay.AddToClassList("mini-board-status-overlay");
                    miniStatusOverlay.style.display = DisplayStyle.None;
                    miniStatusOverlay.pickingMode = PickingMode.Ignore;
                    mini.Add(miniStatusOverlay);

                    miniRow.Add(mini);
                    _miniBoards.Add(mini);
                    _miniBoardByMajor[miniIndex] = mini;
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
