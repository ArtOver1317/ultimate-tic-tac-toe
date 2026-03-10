#nullable enable

using System;
using Runtime.Gameplay;
using Runtime.Games.TicTacToe.Moves;

namespace Runtime.GameModes.Wizard.Online
{
    public static class OnlineMoveIndexCodec
    {
        public static int ToCellIndex(CellId cellId, int minorCount)
        {
            if (minorCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(minorCount), minorCount, "Value must be positive.");

            if (cellId.Major < 0 || cellId.Minor < 0)
                throw new ArgumentOutOfRangeException(nameof(cellId), "Cell coordinates must be non-negative.");

            checked
            {
                return cellId.Major * minorCount + cellId.Minor;
            }
        }

        public static CellId ToCellId(int cellIndex, int minorCount)
        {
            if (cellIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(cellIndex), cellIndex, "Value cannot be negative.");

            if (minorCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(minorCount), minorCount, "Value must be positive.");

            var major = cellIndex / minorCount;
            var minor = cellIndex % minorCount;
            return new CellId(major, minor);
        }

        public static int ResolveMinorCount(FieldRenderSpec spec)
        {
            if (spec == null)
                throw new ArgumentNullException(nameof(spec));

            return spec.Kind switch
            {
                FieldKind.Classic => spec.OuterSize,
                FieldKind.Ultimate => spec.InnerSize * spec.InnerSize,
                _ => throw new InvalidOperationException($"Unsupported field kind: {spec.Kind}"),
            };
        }
    }
}