using System;
using R3;
using Runtime.Games.TicTacToe.Moves;
using Runtime.Games.TicTacToe.Rules;
using Runtime.Games.TicTacToe.Series;
using Runtime.Localization;
using UnityEngine.UIElements;

namespace Runtime.Games.TicTacToe
{
    public enum ResultAction { Restart, Exit }

    /// <summary>
    /// Manages the result popup overlay.
    /// Created once by <see cref="GameplayStartup"/>, reused via <see cref="Show"/> on each round end.
    /// </summary>
    public sealed class GameResultViewModel : IDisposable
    {
        private readonly Subject<ResultAction> _actions = new();

        private readonly VisualElement _overlay;
        private readonly Label _resultLabel;
        private readonly Label _scoreLabel;
        private readonly Label _leadLabel;
        private readonly ILocalizationService _localization;

        /// <summary>
        /// Emits <see cref="ResultAction.Restart"/> or <see cref="ResultAction.Exit"/>
        /// when the user clicks the corresponding button.
        /// </summary>
        public Observable<ResultAction> Actions => _actions;

        public GameResultViewModel(VisualElement parent, ILocalizationService localization = null)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));

            _localization = localization;

            // Build DOM programmatically (inline overlay).
            _overlay = new VisualElement { name = "ResultOverlay" };
            _overlay.AddToClassList("result-overlay");
            _overlay.pickingMode = PickingMode.Position; // blocks input to field behind.
            _overlay.style.display = DisplayStyle.None;

            var popup = new VisualElement { name = "ResultPopup" };
            popup.AddToClassList("result-popup");

            _resultLabel = new Label { name = "ResultLabel" };
            _resultLabel.AddToClassList("result-label");
            popup.Add(_resultLabel);

            _scoreLabel = new Label { name = "ScoreLabel" };
            _scoreLabel.AddToClassList("score-label");
            popup.Add(_scoreLabel);

            _leadLabel = new Label { name = "LeadLabel" };
            _leadLabel.AddToClassList("lead-label");
            popup.Add(_leadLabel);

            var buttons = new VisualElement { name = "ResultButtons" };
            buttons.AddToClassList("result-buttons");

            // TODO: Replace button text with localized strings (Phase 5 localization).
            var restartBtn = new Button(() => _actions.OnNext(ResultAction.Restart))
            {
                name = "RestartButton",
                text = "Restart",
            };
            restartBtn.AddToClassList("result-button");
            buttons.Add(restartBtn);

            var exitBtn = new Button(() => _actions.OnNext(ResultAction.Exit))
            {
                name = "ExitButton",
                text = "Exit",
            };
            exitBtn.AddToClassList("result-button");
            buttons.Add(exitBtn);

            popup.Add(buttons);
            _overlay.Add(popup);
            parent.Add(_overlay);
        }

        /// <summary>
        /// Shows the result popup with current round result and series score.
        /// </summary>
        public void Show(GameResult result, SeriesScore score)
        {
            _resultLabel.text = FormatResult(result);
            _scoreLabel.text = FormatScore(score);
            _leadLabel.text = FormatLead(score);
            _overlay.style.display = DisplayStyle.Flex;
        }

        /// <summary>
        /// Hides the result popup.
        /// </summary>
        public void Hide()
        {
            _overlay.style.display = DisplayStyle.None;
        }

        public void Dispose()
        {
            Hide();
            _overlay.RemoveFromHierarchy();
            _actions.Dispose();
        }

        // TODO: Replace with localized strings from Localization tables.
        private string FormatResult(GameResult result) =>
            result.Winner.ToResultText(result.Status, _localization);

        private static string FormatScore(SeriesScore score) =>
            $"Score: {score.Player1Wins} - {score.Player2Wins}  (Draws: {score.Draws})";

        private static string FormatLead(SeriesScore score)
        {
            var p1 = score.Player1Wins;
            var p2 = score.Player2Wins;
            return p1 > p2 ? "Player 1 leads"
                : p2 > p1 ? "Player 2 leads"
                : "Tied";
        }
    }
}
