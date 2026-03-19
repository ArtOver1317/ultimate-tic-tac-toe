#nullable enable

using System;
using UnityEngine.UIElements;

namespace Runtime.UI.Components
{
    [UxmlElement]
    public sealed partial class MatchmakingTimer : VisualElement
    {
        private const int _neverDisplayedSeconds = -1;

        private readonly Label _label;
        private string _prefix = string.Empty;
        private int _lastDisplayedSeconds = _neverDisplayedSeconds;
        private string _lastPrefix = string.Empty;

        public MatchmakingTimer()
        {
            AddToClassList("matchmaking-timer");

            _label = new Label { name = "TimerLabel" };
            _label.AddToClassList("matchmaking-timer__label");
            Add(_label);

            UpdateText();
        }

        public void SetPrefix(string? prefix)
        {
            _prefix = prefix ?? string.Empty;
            UpdateText();
        }

        public void SetTime(TimeSpan time)
        {
            var normalized = time < TimeSpan.Zero ? TimeSpan.Zero : time;
            var totalSeconds = Math.Max(0, (int)Math.Floor(normalized.TotalSeconds));

            if (_lastDisplayedSeconds == totalSeconds && string.Equals(_lastPrefix, _prefix, StringComparison.Ordinal))
                return;

            _lastDisplayedSeconds = totalSeconds;
            UpdateText();
        }

        private void UpdateText()
        {
            var totalSeconds = Math.Max(0, _lastDisplayedSeconds);
            var minutes = totalSeconds / 60;
            var seconds = totalSeconds % 60;
            var timeText = $"{minutes:00}:{seconds:00}";

            _lastPrefix = _prefix;

            _label.text = string.IsNullOrWhiteSpace(_prefix) ? timeText : $"{_prefix} {timeText}...";
        }
    }
}