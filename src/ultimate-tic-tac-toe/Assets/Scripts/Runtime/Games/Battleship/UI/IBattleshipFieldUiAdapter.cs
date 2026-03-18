#nullable enable
using R3;
using Runtime.Gameplay;
using UnityEngine.UIElements;

namespace Runtime.Games.Battleship.UI
{
    public interface IBattleshipFieldUiAdapter
    {
        Observable<CellId> OwnBoardCellClicks { get; }

        bool HasOwnBoard { get; }

        bool TryGetOwnCell(CellId id, out VisualElement cellRoot);

        bool TryGetOwnCellView(CellId id, out VisualElement cellRoot, out Label markLabel);
    }
}