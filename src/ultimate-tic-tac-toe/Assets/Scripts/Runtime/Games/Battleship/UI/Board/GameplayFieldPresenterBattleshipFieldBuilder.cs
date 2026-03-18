using System;
using Runtime.Gameplay;
using UnityEngine.UIElements;

namespace Runtime.Games.Battleship.UI.Board
{
    internal sealed class GameplayFieldPresenterBattleshipFieldBuilder
    {
        private readonly Func<string, string, string> _resolveGameTextOrFallback;
        private readonly Func<int, int, CellId, VisualElement> _createOpponentCell;
        private readonly Func<int, int, CellId, VisualElement> _createOwnBoardCell;

        public GameplayFieldPresenterBattleshipFieldBuilder(
            Func<string, string, string> resolveGameTextOrFallback,
            Func<int, int, CellId, VisualElement> createOpponentCell,
            Func<int, int, CellId, VisualElement> createOwnBoardCell)
        {
            _resolveGameTextOrFallback = resolveGameTextOrFallback ?? throw new ArgumentNullException(nameof(resolveGameTextOrFallback));
            _createOpponentCell = createOpponentCell ?? throw new ArgumentNullException(nameof(createOpponentCell));
            _createOwnBoardCell = createOwnBoardCell ?? throw new ArgumentNullException(nameof(createOwnBoardCell));
        }

        internal VisualElement Build(FieldRenderSpec spec, Action<VisualElement> registerCell)
        {
            if (spec == null)
                throw new ArgumentNullException(nameof(spec));

            if (registerCell == null)
                throw new ArgumentNullException(nameof(registerCell));

            var size = spec.OuterSize;
            var ownBoardTitle = _resolveGameTextOrFallback("Game.Battleship.OwnBoard", "Your Board");
            var opponentBoardTitle = _resolveGameTextOrFallback("Game.Battleship.OpponentBoard", "Opponent Board");

            var boardsRoot = new VisualElement { name = "BattleshipBoardsRoot" };
            boardsRoot.AddToClassList("battleship-boards");
            boardsRoot.Add(BuildBoard(ownBoardTitle, "battleship-board--own", size, registerCell, createOwnBoardCells: true));
            boardsRoot.Add(BuildBoard(opponentBoardTitle, "battleship-board--opponent", size, registerCell, createOwnBoardCells: false));

            return boardsRoot;
        }

        private VisualElement BuildBoard(
            string title,
            string boardClass,
            int size,
            Action<VisualElement> registerCell,
            bool createOwnBoardCells)
        {
            var boardRoot = new VisualElement();
            boardRoot.AddToClassList("battleship-board");
            boardRoot.AddToClassList(boardClass);

            var titleLabel = new Label { text = title };
            titleLabel.AddToClassList("battleship-board-title");
            boardRoot.Add(titleLabel);

            var grid = new VisualElement();
            grid.AddToClassList("battleship-board-grid");

            for (var y = 0; y < size; y++)
            {
                var row = new VisualElement();
                row.AddToClassList("field-row");

                for (var x = 0; x < size; x++)
                {
                    var cellId = new CellId(y, x);
                    
                    var cell = createOwnBoardCells
                        ? _createOwnBoardCell(x, y, cellId)
                        : _createOpponentCell(x, y, cellId);

                    row.Add(cell);
                    registerCell(cell);
                }

                grid.Add(row);
            }

            boardRoot.Add(grid);
            return boardRoot;
        }
    }
}