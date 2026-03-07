using FluentAssertions;
using NUnit.Framework;
using Runtime.Games.Battleship;
using Runtime.Games.TicTacToe.Moves;

namespace Tests.EditMode.Games.Battleship
{
    [TestFixture]
    [Category("Unit")]
    public class BattleshipPlacementValidatorTests
    {
        [Test]
        public void WhenFleetIsValid_ThenValidationSucceeds()
        {
            var validator = new BattleshipPlacementValidator();
            var autoPlacer = new BattleshipAutoPlacer(validator);
            var layout = autoPlacer.Generate(2026);

            var isValid = validator.TryValidate(layout, out var errorKey);

            isValid.Should().BeTrue();
            errorKey.Should().BeNull();
        }

        [Test]
        public void WhenShipsTouchDiagonally_ThenValidationFails()
        {
            var validator = new BattleshipPlacementValidator();
            var autoPlacer = new BattleshipAutoPlacer(validator);
            var baseLayout = autoPlacer.Generate(777);
            var ships = new ShipPlacement[FleetLayout.ExpectedShipCount];
            for (var i = 0; i < ships.Length; i++)
                ships[i] = baseLayout.Ships![i];

            var anchor = ships[0];
            var targetMajor = anchor.StartCell.Major <= 8 ? anchor.StartCell.Major + 1 : anchor.StartCell.Major - 1;
            var targetMinor = anchor.StartCell.Minor <= 8 ? anchor.StartCell.Minor + 1 : anchor.StartCell.Minor - 1;

            for (var i = 0; i < ships.Length; i++)
            {
                if (ships[i].Size != ShipSize.One)
                    continue;

                ships[i] = new ShipPlacement(ShipSize.One, ShipOrientation.Horizontal, new CellId(targetMajor, targetMinor));
                break;
            }

            var layout = new FleetLayout(ships);

            var isValid = validator.TryValidate(layout, out var errorKey);

            isValid.Should().BeFalse();
            errorKey.Should().Be("Errors.Battleship.Layout.Invalid");
        }

        [Test]
        public void WhenAutoPlacerGeneratesLayout_ThenLayoutIsValid()
        {
            var validator = new BattleshipPlacementValidator();
            var autoPlacer = new BattleshipAutoPlacer(validator);

            var layout = autoPlacer.Generate(12345);

            validator.TryValidate(layout, out var errorKey).Should().BeTrue();
            errorKey.Should().BeNull();
        }

        [Test]
        public void WhenFleetHasWrongShipCounts_ThenValidationFails()
        {
            var validator = new BattleshipPlacementValidator();

            var layout = CreateLayout(
                new ShipPlacement(ShipSize.Four, ShipOrientation.Horizontal, new CellId(0, 0)),
                new ShipPlacement(ShipSize.Four, ShipOrientation.Horizontal, new CellId(2, 0)),
                new ShipPlacement(ShipSize.One, ShipOrientation.Horizontal, new CellId(0, 6)),
                new ShipPlacement(ShipSize.One, ShipOrientation.Horizontal, new CellId(0, 8)),
                new ShipPlacement(ShipSize.One, ShipOrientation.Horizontal, new CellId(2, 6)),
                new ShipPlacement(ShipSize.One, ShipOrientation.Horizontal, new CellId(2, 8)),
                new ShipPlacement(ShipSize.One, ShipOrientation.Horizontal, new CellId(4, 0)),
                new ShipPlacement(ShipSize.One, ShipOrientation.Horizontal, new CellId(4, 2)),
                new ShipPlacement(ShipSize.One, ShipOrientation.Horizontal, new CellId(4, 4)),
                new ShipPlacement(ShipSize.One, ShipOrientation.Horizontal, new CellId(4, 6)));

            var isValid = validator.TryValidate(layout, out var errorKey);

            isValid.Should().BeFalse();
            errorKey.Should().Be("Errors.Battleship.Layout.InvalidFleet");
        }

        [Test]
        public void WhenShipIsOutOfBounds_ThenValidationFails()
        {
            var validator = new BattleshipPlacementValidator();
            var autoPlacer = new BattleshipAutoPlacer(validator);
            var ships = CopyShips(autoPlacer.Generate(31415));

            ships[0] = new ShipPlacement(ShipSize.Four, ShipOrientation.Horizontal, new CellId(0, 8));
            var layout = CreateLayout(ships);

            var isValid = validator.TryValidate(layout, out var errorKey);

            isValid.Should().BeFalse();
            errorKey.Should().Be("Errors.Battleship.Layout.Invalid");
        }

        [Test]
        public void WhenFleetHasOverlappingShips_ThenValidationFails()
        {
            var validator = new BattleshipPlacementValidator();
            var autoPlacer = new BattleshipAutoPlacer(validator);
            var ships = CopyShips(autoPlacer.Generate(27182));

            var firstSingleIndex = -1;
            var secondSingleIndex = -1;
            for (var i = 0; i < ships.Length; i++)
            {
                if (ships[i].Size != ShipSize.One)
                    continue;

                if (firstSingleIndex < 0)
                {
                    firstSingleIndex = i;
                    continue;
                }

                secondSingleIndex = i;
                break;
            }

            ships[secondSingleIndex] = new ShipPlacement(ShipSize.One, ShipOrientation.Horizontal, ships[firstSingleIndex].StartCell);
            var layout = CreateLayout(ships);

            var isValid = validator.TryValidate(layout, out var errorKey);

            isValid.Should().BeFalse();
            errorKey.Should().Be("Errors.Battleship.Layout.Invalid");
        }

        [Test]
        public void WhenValidatorReceivesDefaultFleetLayout_ThenReturnsInvalid()
        {
            var validator = new BattleshipPlacementValidator();

            var isValid = validator.TryValidate(default, out var errorKey);

            isValid.Should().BeFalse();
            errorKey.Should().Be("Errors.Battleship.Layout.Invalid");
        }

        [Test]
        public void WhenAutoPlacerSameSeedCalledTwice_ThenLayoutsAreIdentical()
        {
            var validator = new BattleshipPlacementValidator();
            var autoPlacer = new BattleshipAutoPlacer(validator);
            var serializer = new BattleshipLayoutSerializer();

            var firstLayout = autoPlacer.Generate(42);
            var secondLayout = autoPlacer.Generate(42);

            serializer.Serialize(firstLayout).Should().Be(serializer.Serialize(secondLayout));
        }

        private static FleetLayout CreateLayout(params ShipPlacement[] ships) =>
            new(System.Array.AsReadOnly(ships));

        private static ShipPlacement[] CopyShips(FleetLayout layout)
        {
            var source = layout.Ships!;
            var copy = new ShipPlacement[source.Count];
            for (var i = 0; i < source.Count; i++)
                copy[i] = source[i];

            return copy;
        }

    }
}
