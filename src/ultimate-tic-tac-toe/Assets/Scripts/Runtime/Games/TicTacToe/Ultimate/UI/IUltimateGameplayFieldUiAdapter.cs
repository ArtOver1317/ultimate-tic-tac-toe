using UnityEngine;
using UnityEngine.UIElements;

namespace Runtime.Games.TicTacToe.Ultimate.UI
{
    public interface IUltimateGameplayFieldUiAdapter
    {
        bool TryGetMiniBoard(int major, out VisualElement miniBoardRoot);
        bool TryGetMiniBoardCenter(int major, out Vector2 panelSpaceCenter);
    }

    internal static class UltimateUiHelpers
    {
        internal static void ToggleClass(VisualElement element, string className, bool enabled)
        {
            if (enabled)
                element.AddToClassList(className);
            else
                element.RemoveFromClassList(className);
        }
    }
}
