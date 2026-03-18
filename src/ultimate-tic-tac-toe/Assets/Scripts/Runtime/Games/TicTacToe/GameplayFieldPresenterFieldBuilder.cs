using System;
using System.Diagnostics;
using Runtime.Gameplay;
using Runtime.Games.Battleship.UI.Board;
using Runtime.Infrastructure.Logging;
using StripLog;
using UnityEngine.UIElements;

namespace Runtime.Games.TicTacToe
{
    internal sealed class GameplayFieldPresenterFieldContext
    {
        internal Func<bool> IsReady { get; }
        internal Action OnBackClicked { get; }
        internal Action<CellId> PublishCellClick { get; }
        internal Action<CellId> PublishOwnBoardCellClick { get; }
        internal Func<string, string, string> ResolveGameTextOrFallback { get; }

        public GameplayFieldPresenterFieldContext(
            Func<bool> isReady,
            Action onBackClicked,
            Action<CellId> publishCellClick,
            Action<CellId> publishOwnBoardCellClick,
            Func<string, string, string> resolveGameTextOrFallback)
        {
            IsReady = isReady ?? throw new ArgumentNullException(nameof(isReady));
            OnBackClicked = onBackClicked ?? throw new ArgumentNullException(nameof(onBackClicked));
            PublishCellClick = publishCellClick ?? throw new ArgumentNullException(nameof(publishCellClick));
            PublishOwnBoardCellClick = publishOwnBoardCellClick ?? throw new ArgumentNullException(nameof(publishOwnBoardCellClick));
            ResolveGameTextOrFallback = resolveGameTextOrFallback ?? throw new ArgumentNullException(nameof(resolveGameTextOrFallback));
        }
    }

    internal sealed class GameplayFieldPresenterFieldBuilder
    {
        private readonly UIDocument _uiDocument;
        private readonly GameplayFieldPresenterState _state;
        private readonly GameplayFieldPresenterScoreboardBuilder _scoreboardBuilder;
        private readonly GameplayFieldPresenterCellFactory _cellFactory;
        private readonly GameplayFieldPresenterBattleshipFieldBuilder _battleshipBuilder;
        private readonly GameplayFieldPresenterLayoutController _layoutController;
        private readonly GameplayFieldPresenterFieldContext _context;

        public GameplayFieldPresenterFieldBuilder(
            UIDocument uiDocument,
            GameplayFieldPresenterState state,
            GameplayFieldPresenterScoreboardBuilder scoreboardBuilder,
            GameplayFieldPresenterLayoutController layoutController,
            GameplayFieldPresenterFieldContext context)
        {
            _uiDocument = uiDocument ? uiDocument : throw new ArgumentNullException(nameof(uiDocument));
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _scoreboardBuilder = scoreboardBuilder ?? throw new ArgumentNullException(nameof(scoreboardBuilder));
            _layoutController = layoutController ?? throw new ArgumentNullException(nameof(layoutController));
            _context = context ?? throw new ArgumentNullException(nameof(context));

            _cellFactory = new GameplayFieldPresenterCellFactory(
                _state,
                _context.PublishCellClick,
                _context.PublishOwnBoardCellClick);

            _battleshipBuilder = new GameplayFieldPresenterBattleshipFieldBuilder(
                _context.ResolveGameTextOrFallback,
                _cellFactory.CreateCell,
                _cellFactory.CreateOwnBoardCell);
        }

        internal void Build()
        {
            var spec = _state.Spec ?? throw new InvalidOperationException("GameplayFieldPresenter is missing FieldRenderSpec during build.");
            var stopwatch = Stopwatch.StartNew();

            EnsureFieldRoot();
            EnsureFieldContainer();
            BindBackButton();
            _scoreboardBuilder.EnsureCurrentPlayerLabelExists();
            ResetFieldState();
            BuildField(spec);
            _layoutController.AttachToFieldContainer(_state.FieldContainer);

            stopwatch.Stop();
            Log.Info(LogTags.UI, $"[GameplayFieldPresenter] Field build: {spec.Kind}, {stopwatch.ElapsedMilliseconds} ms");
        }

        internal bool TryGetCell(CellId id, out VisualElement cellRoot)
        {
            if (!_context.IsReady() || _state.Spec == null || !_state.IsCellIdCacheValid)
            {
                cellRoot = null;
                return false;
            }

            return _state.CellById.TryGetValue(id, out cellRoot);
        }

        internal bool TryGetMark(CellId id, out VisualElement mark)
        {
            if (!_context.IsReady() || _state.Spec == null || !_state.IsCellIdCacheValid)
            {
                mark = null;
                return false;
            }

            return _state.MarkById.TryGetValue(id, out mark);
        }

        internal void OnCellClicked(VisualElement cell)
        {
            if (!_context.IsReady() || cell?.userData is not CellUserData userData)
                return;

            _context.PublishCellClick(userData.CellId);
        }

        private void EnsureFieldRoot()
        {
            _state.Root = _uiDocument.rootVisualElement ?? throw new InvalidOperationException("UIDocument root is null.");
            var fieldRoot = _state.Root.Q<VisualElement>("GameplayFieldRoot");

            if (fieldRoot == null)
            {
                fieldRoot = new VisualElement { name = "GameplayFieldRoot" };
                fieldRoot.AddToClassList("gameplay-field-root");
                _state.Root.Add(fieldRoot);
            }

            fieldRoot.AddToClassList("gameplay-field-root");
            _state.FieldRoot = fieldRoot;
            _layoutController.AttachToFieldRoot(_state.FieldRoot);
        }

        private void EnsureFieldContainer()
        {
            _state.FieldContainer = _state.FieldRoot.Q<VisualElement>("FieldContainer");

            if (_state.FieldContainer != null)
                return;

            _state.FieldContainer = new VisualElement { name = "FieldContainer" };
            _state.FieldContainer.AddToClassList("field-container");
            _state.FieldRoot.Add(_state.FieldContainer);
        }

        private void BindBackButton()
        {
            _state.BackButton = _state.FieldRoot.Q<Button>("BackButton");

            if (_state.BackButton != null)
                _state.BackButton.clicked += _context.OnBackClicked;
            else
                Log.Error(LogTags.UI, "[GameplayFieldPresenter] BackButton not found.");
        }

        private void ResetFieldState()
        {
            _state.FieldContainer.Clear();
            _state.FieldContainer.RemoveFromClassList("field-container--classic");
            _state.FieldContainer.RemoveFromClassList("field-container--ultimate");
            _state.FieldContainer.RemoveFromClassList("field-container--battleship");

            _state.ClearCellCaches();
            _state.IsCellIdCacheValid = true;
            _state.BattleshipBoardsRoot = null;
        }

        private void BuildField(FieldRenderSpec spec)
        {
            if (_state.CurrentMode == GameplayFieldPresenterMode.BattleshipDual)
            {
                _state.FieldContainer.AddToClassList("field-container--battleship");
                _state.BattleshipBoardsRoot = _battleshipBuilder.Build(spec, AddRenderedCell);
                _state.FieldContainer.Add(_state.BattleshipBoardsRoot);
                return;
            }

            if (_state.CurrentMode == GameplayFieldPresenterMode.Ultimate)
            {
                _state.FieldContainer.AddToClassList("field-container--ultimate");
                BuildUltimate(spec);
                return;
            }

            _state.FieldContainer.AddToClassList("field-container--classic");
            BuildClassic(spec);
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
                    var cell = _cellFactory.CreateCell(x, y, cellId);
                    row.Add(cell);
                    AddRenderedCell(cell);
                }

                _state.FieldContainer.Add(row);
            }
        }

        private void BuildUltimate(FieldRenderSpec spec)
        {
            for (var miniY = 0; miniY < spec.OuterSize; miniY++)
            {
                _state.FieldContainer.Add(CreateUltimateMiniRow(miniY, spec.OuterSize, spec.InnerSize));
            }
        }

        private VisualElement CreateUltimateMiniRow(int miniY, int outerSize, int innerSize)
        {
            var miniRow = new VisualElement();
            miniRow.AddToClassList("mini-row");

            for (var miniX = 0; miniX < outerSize; miniX++)
            {
                miniRow.Add(CreateUltimateMiniBoard(miniX, miniY, outerSize, innerSize));
            }

            return miniRow;
        }

        private VisualElement CreateUltimateMiniBoard(int miniX, int miniY, int outerSize, int innerSize)
        {
            var mini = new VisualElement { name = $"Mini_{miniX}_{miniY}" };
            mini.AddToClassList("mini-board");

            var miniIndex = miniY * outerSize + miniX;
            AddUltimateCellRows(mini, miniIndex, innerSize);
            AddMiniStatusOverlay(mini);

            _state.MiniBoards.Add(mini);
            _state.MiniBoardByMajor[miniIndex] = mini;

            return mini;
        }

        private void AddUltimateCellRows(VisualElement miniBoard, int miniIndex, int innerSize)
        {
            for (var y = 0; y < innerSize; y++)
            {
                miniBoard.Add(CreateUltimateCellRow(y, innerSize, miniIndex));
            }
        }

        private VisualElement CreateUltimateCellRow(int rowIndex, int innerSize, int miniIndex)
        {
            var row = new VisualElement();
            row.AddToClassList("mini-row-inner");

            for (var x = 0; x < innerSize; x++)
            {
                var minor = rowIndex * innerSize + x;
                var cellId = new CellId(miniIndex, minor);
                var cell = _cellFactory.CreateCell(x, rowIndex, cellId);
                row.Add(cell);
                AddRenderedCell(cell);
            }

            return row;
        }

        private static void AddMiniStatusOverlay(VisualElement miniBoard)
        {
            var miniStatusOverlay = new Label { name = "MiniStatusOverlay", text = string.Empty };
            miniStatusOverlay.AddToClassList("mini-board-status-overlay");
            miniStatusOverlay.style.display = DisplayStyle.None;
            miniStatusOverlay.pickingMode = PickingMode.Ignore;
            miniBoard.Add(miniStatusOverlay);
        }

        private void AddRenderedCell(VisualElement cell)
        {
            if (cell != null)
                _state.Cells.Add(cell);
        }
    }
}
