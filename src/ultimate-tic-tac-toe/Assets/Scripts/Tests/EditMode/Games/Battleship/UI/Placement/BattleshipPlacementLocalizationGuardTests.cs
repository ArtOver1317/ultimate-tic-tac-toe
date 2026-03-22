#nullable enable

using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using NUnit.Framework;
using UnityEngine;

namespace Tests.EditMode.Games.Battleship.UI.Placement
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
        public void WhenCheckingPlacementTextBinderSource_ThenUsesPlacementLocalizationKeys()
        {
            var textBinderSource = ReadSourceFile("BattleshipPlacementPanelTextBinder.cs");

            textBinderSource.Should().Contain("Game.Battleship.Placement.AutoButton");
            textBinderSource.Should().Contain("Game.Battleship.Placement.RotateButton");
            textBinderSource.Should().Contain("Game.Battleship.Placement.RemoveButton");
            textBinderSource.Should().Contain("Game.Battleship.Placement.ReadyButton");
            textBinderSource.Should().Contain("Game.Battleship.Placement.Status.WaitingOpponent");
            textBinderSource.Should().Contain("Game.Battleship.Placement.Status.Unavailable");
            textBinderSource.Should().Contain("Game.Battleship.Placement.Status.PlaceAllShips");
            textBinderSource.Should().Contain("Game.Battleship.Placement.Status.ConfirmReady");
        }

        private static string ReadPlacementUiControllerSource() =>
            ReadSourceFile("BattleshipPlacementUiController.cs");

        private static string ReadSourceFile(string fileName)
        {
            var sourcePath = Path.Combine(
                Application.dataPath,
                "Scripts",
                "Runtime",
                "Games",
                "Battleship",
                "UI",
                "Placement",
                fileName);

            File.Exists(sourcePath).Should().BeTrue($"{fileName} source file must exist");
            return File.ReadAllText(sourcePath);
        }
    }
}
