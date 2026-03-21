using FluentAssertions;
using NUnit.Framework;
using Runtime.Gameplay;
using Runtime.Games.TicTacToe.Moves;

namespace Tests.EditMode.Games.TicTacToe.UI.Board
{
    [TestFixture]
    [Category("Unit")]
    public class FieldRenderSpecExtensionsTests
    {
        [Test]
        public void WhenClassic3x3AndCellWithinBounds_ThenReturnsTrue()
        {
            // Arrange
            var spec = FieldRenderSpec.Classic(3);

            // Act
            var result = spec.IsValidCellId(new CellId(2, 2));

            // Assert
            result.Should().BeTrue();
        }

        [Test]
        public void WhenClassic5x5AndCellIsOutOfBounds_ThenReturnsFalse()
        {
            // Arrange
            var spec = FieldRenderSpec.Classic(5);

            // Act
            var result = spec.IsValidCellId(new CellId(5, 0));

            // Assert
            result.Should().BeFalse();
        }

        [Test]
        public void WhenClassic3x3AndMinorIsOutOfBounds_ThenReturnsFalse()
        {
            // Arrange
            var spec = FieldRenderSpec.Classic(3);

            // Act
            var result = spec.IsValidCellId(new CellId(0, 3));

            // Assert
            result.Should().BeFalse();
        }

        [Test]
        public void WhenClassicAndCellHasNegativeIndex_ThenReturnsFalse()
        {
            // Arrange
            var spec = FieldRenderSpec.Classic(3);

            // Act
            var result = spec.IsValidCellId(new CellId(-1, 0));

            // Assert
            result.Should().BeFalse();
        }

        [Test]
        public void WhenClassicAndMinorIsNegative_ThenReturnsFalse()
        {
            // Arrange
            var spec = FieldRenderSpec.Classic(3);

            // Act
            var result = spec.IsValidCellId(new CellId(0, -1));

            // Assert
            result.Should().BeFalse();
        }

        [Test]
        public void WhenUltimateAndCellWithinFlatIndexBounds_ThenReturnsTrue()
        {
            // Arrange
            var spec = FieldRenderSpec.Ultimate();

            // Act
            var result = spec.IsValidCellId(new CellId(8, 8));

            // Assert
            result.Should().BeTrue();
        }

        [Test]
        public void WhenUltimateAndCellIsOutOfBounds_ThenReturnsFalse()
        {
            // Arrange
            var spec = FieldRenderSpec.Ultimate();

            // Act
            var result = spec.IsValidCellId(new CellId(9, 0));

            // Assert
            result.Should().BeFalse();
        }

        [Test]
        public void WhenUltimateAndMinorIsOutOfBounds_ThenReturnsFalse()
        {
            // Arrange
            var spec = FieldRenderSpec.Ultimate();

            // Act
            var result = spec.IsValidCellId(new CellId(0, 9));

            // Assert
            result.Should().BeFalse();
        }

        [Test]
        public void WhenUltimateAndIndicesAreNegative_ThenReturnsFalse()
        {
            // Arrange
            var spec = FieldRenderSpec.Ultimate();

            // Act
            var result = spec.IsValidCellId(new CellId(-1, -1));

            // Assert
            result.Should().BeFalse();
        }
    }
}
