using R3;
using UnityEngine.UIElements;

namespace Runtime.Gameplay.Moves
{
    public interface IGameplayFieldUiAdapter
    {
        Observable<CellId> CellClicks { get; }

        bool TryGetCell(CellId id, out VisualElement cellRoot);

        bool TryGetMark(CellId id, out VisualElement mark);

        Label CurrentPlayerLabel { get; }
    }
}
