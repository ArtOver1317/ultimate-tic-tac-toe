#nullable enable

using Runtime.Gameplay;

namespace Runtime.Games.TicTacToe.Moves
{
    public static class FieldRenderSpecExtensions
    {
        public static bool IsValidCellId(this FieldRenderSpec? spec, CellId cellId)
        {
            if (spec == null)
                return false;

            if (cellId.Major < 0 || cellId.Minor < 0)
                return false;

            return spec.Kind switch
            {
                FieldKind.Classic => cellId.Major < spec.OuterSize && cellId.Minor < spec.OuterSize,
                FieldKind.Ultimate => cellId.Major < (long)spec.OuterSize * spec.OuterSize
                                      && cellId.Minor < (long)spec.InnerSize * spec.InnerSize,
                _ => false,
            };
        }
    }
}