using R3;
using UnityEngine.UIElements;

namespace Runtime.Games.TicTacToe.Moves
{
    public interface IGameplayFieldUiAdapter
    {
        Observable<CellId> CellClicks { get; }

        bool TryGetCellView(CellId id, out VisualElement cellRoot, out Label markLabel);

        bool TryGetCell(CellId id, out VisualElement cellRoot);

        bool TryGetMark(CellId id, out VisualElement mark);

        Label CurrentPlayerLabel { get; }
    }
}
