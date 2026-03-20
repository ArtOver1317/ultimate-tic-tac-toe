#nullable enable
using System;
using System.Collections.Generic;
using R3;
using Runtime.Games.Battleship.Startup;
using Runtime.Games.TicTacToe;
using Runtime.Games.TicTacToe.Series;
using Runtime.Localization;
using Runtime.Localization.Contracts;
using Runtime.Localization.Types;
using UnityEngine.UIElements;

namespace Runtime.Gameplay.Startup
{
    public enum ResultAction
    {
        Restart, 
        Exit,
    }

    /// <summary>
    /// Manages the result popup overlay.
    /// Created once by <see cref="TicTacToeGameplayStartup"/> or <see cref="BattleshipGameplayStartup"/>, reused via <see cref="Show"/> on each round end.
    /// </summary>
    public sealed class GameResultViewModel : IDisposable
    {
        private readonly Subject<ResultAction> _actions = new();

        private readonly VisualElement _overlay;
        private readonly Label _resultLabel;
        private readonly Label _scoreLabel;
        private readonly Label _leadLabel;
        private readonly ILocalizationService? _localization;

        /// <summary>
        /// Emits <see cref="ResultAction.Restart"/> or <see cref="ResultAction.Exit"/>
        /// when the user clicks the corresponding button.
        /// </summary>
        public Observable<ResultAction> Actions => _actions;

        public GameResultViewModel(VisualElement parent, ILocalizationService? localization = null)
        {
            if (parent == null) 
                throw new ArgumentNullException(nameof(parent));

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

            var restartBtn = new Button(() => _actions.OnNext(ResultAction.Restart))
            {
                name = "RestartButton",
                text = ResolveTextOrFallback("Game", "Game.Result.RestartButton", "Restart"),
            };
            
            restartBtn.AddToClassList("result-button");
            buttons.Add(restartBtn);

            var exitBtn = new Button(() => _actions.OnNext(ResultAction.Exit))
            {
                name = "ExitButton",
                text = ResolveTextOrFallback("Game", "Game.Result.ExitButton", "Exit"),
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
        public void Show(GameResult result, SeriesScore score, string? customResultText = null)
        {
            _resultLabel.text = string.IsNullOrWhiteSpace(customResultText)
                ? FormatResult(result)
                : customResultText;
            
            _scoreLabel.text = FormatScore(score);
            _leadLabel.text = FormatLead(score);
            _overlay.style.display = DisplayStyle.Flex;
        }

        /// <summary>
        /// Hides the result popup.
        /// </summary>
        public void Hide() => _overlay.style.display = DisplayStyle.None;

        public void Dispose()
        {
            Hide();
            _overlay.RemoveFromHierarchy();
            _actions.Dispose();
        }

        private string FormatResult(GameResult result) =>
            result.Winner.ToResultText(result.Status, _localization);

        private string FormatScore(SeriesScore score)
        {
            var fallback = $"Score: {score.Player1Wins} - {score.Player2Wins}  (Draws: {score.Draws})";
            
            var args = new Dictionary<string, object>
            {
                ["player1Wins"] = score.Player1Wins,
                ["player2Wins"] = score.Player2Wins,
                ["draws"] = score.Draws,
            };

            return ResolveTextOrFallback("Game", "Game.Result.Score", fallback, args);
        }

        private string FormatLead(SeriesScore score)
        {
            var p1 = score.Player1Wins;
            var p2 = score.Player2Wins;

            if (p1 > p2)
                return ResolveTextOrFallback("Game", "Game.Result.LeadPlayer1", "Player 1 leads");

            return p2 > p1 
                ? ResolveTextOrFallback("Game", "Game.Result.LeadPlayer2", "Player 2 leads") 
                : ResolveTextOrFallback("Game", "Game.Result.Tied", "Tied");
        }

        private string ResolveTextOrFallback(
            string table,
            string key,
            string fallback,
            IReadOnlyDictionary<string, object>? args = null)
        {
            if (_localization == null)
                return fallback;

            return _localization.TryResolve(table, key, out var resolved, args) ? resolved : fallback;
        }
    }
}
