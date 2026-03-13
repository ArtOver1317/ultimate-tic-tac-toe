#nullable enable

using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using NUnit.Framework;
using UnityEngine;

namespace Tests.EditMode.Games.Battleship
{
    [TestFixture]
    [Category("Unit")]
    public sealed class BattleshipPlacementLocalizationGuardTests
    {
        [Test]
        public void WhenCheckingPlacementUiControllerSource_ThenNoCyrillicStringLiteralsRemain()
        {
            var uiControllerSource = ReadPlacementUiControllerSource();
            var stringLiteralRegex = new Regex("\"(?:\\\\.|[^\"])*\"", RegexOptions.CultureInvariant);
            var cyrillicRegex = new Regex("[\\u0400-\\u04FF]", RegexOptions.CultureInvariant);

            var cyrillicLiterals = stringLiteralRegex
                .Matches(uiControllerSource)
                .Select(match => match.Value)
                .Where(literal => cyrillicRegex.IsMatch(literal))
                .ToArray();

            cyrillicLiterals.Should().BeEmpty(
                "placement UI text must be localized via keys instead of hardcoded Cyrillic literals");
        }

        [Test]
        public void WhenCheckingPlacementUiControllerSource_ThenUsesPlacementLocalizationKeys()
        {
            var uiControllerSource = ReadPlacementUiControllerSource();

            uiControllerSource.Should().Contain("Game.Battleship.Placement.AutoButton");
            uiControllerSource.Should().Contain("Game.Battleship.Placement.RotateButton");
            uiControllerSource.Should().Contain("Game.Battleship.Placement.RemoveButton");
            uiControllerSource.Should().Contain("Game.Battleship.Placement.ReadyButton");
            uiControllerSource.Should().Contain("Game.Battleship.Placement.Status.WaitingOpponent");
            uiControllerSource.Should().Contain("Game.Battleship.Placement.Status.Unavailable");
            uiControllerSource.Should().Contain("Game.Battleship.Placement.Status.PlaceAllShips");
            uiControllerSource.Should().Contain("Game.Battleship.Placement.Status.ConfirmReady");
        }

        private static string ReadPlacementUiControllerSource()
        {
            var sourcePath = Path.Combine(
                Application.dataPath,
                "Scripts",
                "Runtime",
                "Games",
                "Battleship",
                "UI",
                "Placement",
                "BattleshipPlacementUiController.cs");

            File.Exists(sourcePath).Should().BeTrue("placement UI controller source file must exist");
            return File.ReadAllText(sourcePath);
        }
    }
}
