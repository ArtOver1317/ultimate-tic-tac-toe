#nullable enable

using Runtime.Games.Battleship.Core;
using UnityEngine.UIElements;

namespace Runtime.Games.Battleship.UI.Board
{
    /// <summary>
    /// Stateless helper that maps <see cref="BattleshipCellMark"/> to CSS classes and text on board cells.
    /// </summary>
    internal static class BattleshipBoardCellRenderer
    {
        internal const string ShipClass = "battleship-mark--ship";
        internal const string MissClass = "battleship-mark--miss";
        internal const string HitClass = "battleship-mark--hit";
        internal const string SunkClass = "battleship-mark--sunk";
        internal const string OwnShipClass = "battleship-own--ship";
        internal const string OwnHitClass = "battleship-own--hit";
        internal const string OwnSunkClass = "battleship-own--sunk";
        internal const string OpponentHitClass = "battleship-opponent--hit";
        internal const string OpponentSunkClass = "battleship-opponent--sunk";

        private const string _missChar = "\u2022";
        private const string _hitChar = "X";

        internal static (string text, string? cssClass) ResolveOpponentMark(BattleshipCellMark mark) =>
            mark switch
            {
                BattleshipCellMark.Miss => (_missChar, MissClass),
                BattleshipCellMark.Hit => (_hitChar, HitClass),
                BattleshipCellMark.Sunk => (_hitChar, SunkClass),
                _ => (string.Empty, null),
            };

        internal static (string text, string? cssClass) ResolveOwnMark(BattleshipCellMark mark, bool hasShip) =>
            mark switch
            {
                BattleshipCellMark.Miss => (_missChar, MissClass),
                BattleshipCellMark.Hit => (_hitChar, OwnHitClass),
                BattleshipCellMark.Sunk => (_hitChar, OwnSunkClass),
                _ => (string.Empty, null),
            };

        internal static void ApplyMark(Label markLabel, string text, string? cssClass)
        {
            markLabel.text = text;
            markLabel.RemoveFromClassList("mark-label--x");
            markLabel.RemoveFromClassList("mark-label--o");
            markLabel.RemoveFromClassList(ShipClass);
            markLabel.RemoveFromClassList(MissClass);
            markLabel.RemoveFromClassList(HitClass);
            markLabel.RemoveFromClassList(SunkClass);
            markLabel.RemoveFromClassList(OwnHitClass);
            markLabel.RemoveFromClassList(OwnSunkClass);
            markLabel.RemoveFromClassList(OpponentHitClass);
            markLabel.RemoveFromClassList(OpponentSunkClass);

            if (!string.IsNullOrEmpty(cssClass))
                markLabel.AddToClassList(cssClass);
        }

        internal static void ApplyOpponentCellClass(VisualElement? cellRoot, BattleshipCellMark mark)
        {
            if (cellRoot == null)
                return;

            cellRoot.RemoveFromClassList(OpponentHitClass);
            cellRoot.RemoveFromClassList(OpponentSunkClass);

            if (mark == BattleshipCellMark.Hit)
                cellRoot.AddToClassList(OpponentHitClass);
            else if (mark == BattleshipCellMark.Sunk)
                cellRoot.AddToClassList(OpponentSunkClass);
        }

        internal static void ApplyOwnCellClass(VisualElement? cellRoot, BattleshipCellMark mark, bool hasShip)
        {
            if (cellRoot == null)
                return;

            cellRoot.RemoveFromClassList(OwnShipClass);
            cellRoot.RemoveFromClassList(OwnHitClass);
            cellRoot.RemoveFromClassList(OwnSunkClass);

            if (mark == BattleshipCellMark.Hit)
                cellRoot.AddToClassList(OwnHitClass);
            else if (mark == BattleshipCellMark.Sunk)
                cellRoot.AddToClassList(OwnSunkClass);
            else if (hasShip)
                cellRoot.AddToClassList(OwnShipClass);
        }
    }
}
