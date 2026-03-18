using R3;
using UnityEngine.UIElements;

namespace Runtime.Gameplay
{
    public interface IGameplayFieldUiAdapter
    {
        Observable<CellId> CellClicks { get; }

        bool TryGetCellView(CellId id, out VisualElement cellRoot, out Label markLabel);

        bool TryGetCell(CellId id, out VisualElement cellRoot);

        bool TryGetMark(CellId id, out VisualElement mark);

        Label CurrentPlayerLabel { get; }

        VisualElement FieldContainer { get; }

        VisualElement Player1Panel { get; }

        VisualElement Player2Panel { get; }

        Label Player1ScoreLabel { get; }

        Label Player1NameLabel { get; }

        Label Player2ScoreLabel { get; }

        Label Player2NameLabel { get; }

        Label DrawsScoreLabel { get; }

        Label MoveTimerLabel { get; }
    }
}