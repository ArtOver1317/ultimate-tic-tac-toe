#nullable enable

using System;

namespace Runtime.Games.Battleship.UI.Placement
{
    public interface IBattleshipPlacementUiController : IDisposable
    {
        void Bind();

        void Unbind();
    }
}