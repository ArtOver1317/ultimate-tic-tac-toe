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

        
    }
}
