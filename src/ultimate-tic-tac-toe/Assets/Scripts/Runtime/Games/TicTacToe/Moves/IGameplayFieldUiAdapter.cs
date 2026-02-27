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

        /// <summary>
        /// The field grid container. Used by overlay renderers (e.g. WinLineRenderer)
        /// to position absolute-positioned elements relative to the field layout.
        /// Returns null when the presenter is not bound.
        /// </summary>
        VisualElement FieldContainer { get; }

        // ── Scoreboard elements ──

        /// <summary>Player 1 panel. Toggle "player-panel--active" to highlight.</summary>
        VisualElement Player1Panel { get; }

        /// <summary>Player 2 panel. Toggle "player-panel--active" to highlight.</summary>
        VisualElement Player2Panel { get; }

        /// <summary>Score label for Player 1 (shows win count).</summary>
        Label Player1ScoreLabel { get; }

        /// <summary>Name label for Player 1.</summary>
        Label Player1NameLabel { get; }

        /// <summary>Score label for Player 2 (shows win count).</summary>
        Label Player2ScoreLabel { get; }

        /// <summary>Name label for Player 2.</summary>
        Label Player2NameLabel { get; }

        /// <summary>Score label for draws counter.</summary>
        Label DrawsScoreLabel { get; }

        /// <summary>HUD label for current move timer.</summary>
        Label MoveTimerLabel { get; }
    }
}
