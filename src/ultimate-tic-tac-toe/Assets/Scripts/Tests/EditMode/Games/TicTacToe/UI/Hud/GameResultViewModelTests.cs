using System.Collections.Generic;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using Runtime.Gameplay;
using Runtime.Gameplay.Startup;
using Runtime.Games.TicTacToe.Series;
using Runtime.Localization.Contracts;
using Runtime.Localization.Types;
using UnityEngine.UIElements;

namespace Tests.EditMode.Games.TicTacToe.UI.Hud
{
    [TestFixture]
    [Category("Unit")]
    public class GameResultViewModelTests
    {
        private VisualElement _parent;
        private GameResultViewModel _vm;

        [SetUp]
        public void SetUp()
        {
            _parent = new VisualElement { name = "TestParent" };
            _vm = new GameResultViewModel(_parent);
        }

        [TearDown]
        public void TearDown() => _vm?.Dispose();

        // ── Visibility ──

        [Test]
        public void WhenCreated_ThenOverlayIsHidden()
        {
            // Assert
            var overlay = _parent.Q<VisualElement>("ResultOverlay");
            overlay.Should().NotBeNull();
            overlay.style.display.value.Should().Be(DisplayStyle.None);
        }

        [Test]
        public void WhenShowCalled_ThenOverlayIsVisible()
        {
            // Act
            _vm.Show(GameResult.Win(PlayerMark.X, new WinLine(default, default, WinLineDirection.Horizontal, 3)),
                default(SeriesScore));

            // Assert
            var overlay = _parent.Q<VisualElement>("ResultOverlay");
            overlay.style.display.value.Should().Be(DisplayStyle.Flex);
        }

        [Test]
        public void WhenHideCalled_ThenOverlayIsHidden()
        {
            // Arrange
            _vm.Show(GameResult.Draw(), default);

            // Act
            _vm.Hide();

            // Assert
            var overlay = _parent.Q<VisualElement>("ResultOverlay");
            overlay.style.display.value.Should().Be(DisplayStyle.None);
        }

        // ── Label content ──

        [Test]
        public void WhenShowWithWin_ThenResultLabelShowsWinnerText()
        {
            // Act
            _vm.Show(GameResult.Win(PlayerMark.X, new WinLine(default, default, WinLineDirection.Horizontal, 3)),
                default);

            // Assert
            var label = _parent.Q<Label>("ResultLabel");
            label.text.Should().Contain("Player 1 (X) Wins!");
        }

        [Test]
        public void WhenShowWithLocalization_ThenResultLabelShowsLocalizedWinnerText()
        {
            _vm.Dispose();

            var localization = Substitute.For<ILocalizationService>();
            
            localization.TryResolve(
                    Arg.Is<TextTableId>(table => table.Name == "GameOver"),
                    Arg.Is<TextKey>(key => key.Value == "GameOver.Win.Player1"),
                    out Arg.Any<string>(),
                    Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns(callInfo =>
                {
                    callInfo[2] = "Игрок 1 (X) победил!";
                    return true;
                });

            _vm = new GameResultViewModel(_parent, localization);

            _vm.Show(GameResult.Win(PlayerMark.X, new WinLine(default, default, WinLineDirection.Horizontal, 3)),
                default);

            var label = _parent.Q<Label>("ResultLabel");
            label.text.Should().Be("Игрок 1 (X) победил!");
        }

        [Test]
        public void WhenShowWithDraw_ThenResultLabelShowsDraw()
        {
            // Act
            _vm.Show(GameResult.Draw(), default);

            // Assert
            var label = _parent.Q<Label>("ResultLabel");
            label.text.Should().Contain("Draw!");
        }

        [Test]
        public void WhenShowWithScore_ThenScoreLabelShowsScoreAndLeadLabelShowsLead()
        {
            // Act
            var score = new SeriesScore(3, 1, 2, 6);
            _vm.Show(GameResult.Draw(), score);

            // Assert
            var scoreLabel = _parent.Q<Label>("ScoreLabel");
            scoreLabel.text.Should().Contain("3 - 1");
            scoreLabel.text.Should().Contain("Draws: 2");

            var leadLabel = _parent.Q<Label>("LeadLabel");
            leadLabel.Should().NotBeNull();
            leadLabel.text.Should().Contain("Player 1 leads");
        }

        // ── Actions / Buttons structure ──

        [Test]
        public void WhenCreated_ThenRestartButtonExists()
        {
            // Assert
            var btn = _parent.Q<Button>("RestartButton");
            btn.Should().NotBeNull();
            btn.text.Should().Be("Restart");
        }

        [Test]
        public void WhenCreated_ThenExitButtonExists()
        {
            // Assert
            var btn = _parent.Q<Button>("ExitButton");
            btn.Should().NotBeNull();
            btn.text.Should().Be("Exit");
        }

        // ── Dispose ──

        [Test]
        public void WhenDisposed_ThenOverlayRemovedFromParent()
        {
            // Act
            _vm.Dispose();
            _vm = null;

            // Assert
            _parent.Q<VisualElement>("ResultOverlay").Should().BeNull();
        }
    }
}
