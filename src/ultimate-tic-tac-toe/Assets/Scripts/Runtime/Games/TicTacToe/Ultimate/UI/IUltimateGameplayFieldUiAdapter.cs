using UnityEngine;
using UnityEngine.UIElements;

namespace Runtime.Games.TicTacToe.Ultimate.UI
{
    public interface IUltimateGameplayFieldUiAdapter
    {
        bool TryGetMiniBoard(int major, out VisualElement miniBoardRoot);
        bool TryGetMiniBoardCenter(int major, out Vector2 panelSpaceCenter);
    }
}
