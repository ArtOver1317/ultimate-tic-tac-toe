using System;
using UnityEngine.UIElements;

namespace Runtime.Games.TicTacToe
{
    internal sealed class GameplayFieldPresenterScoreboardBuilder
    {
        private readonly GameplayFieldPresenterState _state;

        public GameplayFieldPresenterScoreboardBuilder(GameplayFieldPresenterState state) => 
            _state = state ?? throw new ArgumentNullException(nameof(state));

        internal void EnsureCurrentPlayerLabelExists()
        {
            if (_state.FieldRoot == null)
                return;

            BuildScoreboard();
        }

        private void BuildScoreboard()
        {
            var existing = _state.FieldRoot.Q<VisualElement>("Scoreboard");
            
            if (existing != null)
            {
                AcquireScoreboardReferences(existing);
                return;
            }

            var scoreboard = new VisualElement { name = "Scoreboard" };
            scoreboard.AddToClassList("scoreboard");

            var p1Panel = new VisualElement { name = "Player1Panel" };
            p1Panel.AddToClassList("player-panel");

            var p1Name = new Label { name = "Player1Name", text = "Player 1 (X)" };
            p1Name.AddToClassList("player-name");
            p1Panel.Add(p1Name);

            var p1Score = new Label { name = "Player1Score", text = "0" };
            p1Score.AddToClassList("player-score");
            p1Panel.Add(p1Score);

            scoreboard.Add(p1Panel);

            var centerLabel = new Label { name = "CurrentPlayerLabel", text = string.Empty };
            centerLabel.AddToClassList("current-player-label");
            scoreboard.Add(centerLabel);

            var moveTimerLabel = new Label { name = "MoveTimerLabel", text = "00" };
            moveTimerLabel.AddToClassList("player-score");
            moveTimerLabel.AddToClassList("move-timer-label");
            moveTimerLabel.style.display = DisplayStyle.None;
            scoreboard.Add(moveTimerLabel);

            var drawsScore = new Label { name = "DrawsScore", text = "D:0" };
            drawsScore.AddToClassList("player-score");
            scoreboard.Add(drawsScore);

            var p2Panel = new VisualElement { name = "Player2Panel" };
            p2Panel.AddToClassList("player-panel");

            var p2Name = new Label { name = "Player2Name", text = "Player 2 (O)" };
            p2Name.AddToClassList("player-name");
            p2Panel.Add(p2Name);

            var p2Score = new Label { name = "Player2Score", text = "0" };
            p2Score.AddToClassList("player-score");
            p2Panel.Add(p2Score);

            scoreboard.Add(p2Panel);

            var toolbar = _state.FieldRoot.Q<VisualElement>("GameplayToolbar");
            
            if (toolbar != null)
            {
                var index = _state.FieldRoot.IndexOf(toolbar);
                _state.FieldRoot.Insert(index + 1, scoreboard);
            }
            else
                _state.FieldRoot.Insert(0, scoreboard);

            AcquireScoreboardReferences(scoreboard);
        }

        private void AcquireScoreboardReferences(VisualElement scoreboard)
        {
            _state.Player1Panel = scoreboard.Q<VisualElement>("Player1Panel");
            _state.Player2Panel = scoreboard.Q<VisualElement>("Player2Panel");
            _state.Player1NameLabel = scoreboard.Q<Label>("Player1Name");
            _state.Player2NameLabel = scoreboard.Q<Label>("Player2Name");
            _state.Player1ScoreLabel = scoreboard.Q<Label>("Player1Score");
            _state.Player2ScoreLabel = scoreboard.Q<Label>("Player2Score");
            _state.DrawsScoreLabel = scoreboard.Q<Label>("DrawsScore");
            _state.MoveTimerLabel = scoreboard.Q<Label>("MoveTimerLabel");

            if (_state.MoveTimerLabel == null)
            {
                _state.MoveTimerLabel = new Label { name = "MoveTimerLabel", text = "00" };
                _state.MoveTimerLabel.AddToClassList("player-score");
                _state.MoveTimerLabel.AddToClassList("move-timer-label");
                _state.MoveTimerLabel.style.display = DisplayStyle.None;

                var currentPlayerLabel = scoreboard.Q<Label>("CurrentPlayerLabel");
                
                if (currentPlayerLabel != null)
                {
                    var index = scoreboard.IndexOf(currentPlayerLabel);
                    scoreboard.Insert(index + 1, _state.MoveTimerLabel);
                }
                else
                    scoreboard.Insert(0, _state.MoveTimerLabel);
            }

            _state.CurrentPlayerLabel = scoreboard.Q<Label>("CurrentPlayerLabel");
        }
    }
}