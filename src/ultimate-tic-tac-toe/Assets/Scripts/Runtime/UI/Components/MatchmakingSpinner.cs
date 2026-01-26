#nullable enable

using System;
using UnityEngine.UIElements;

namespace Runtime.UI.Components
{
    [UxmlElement]
    public sealed partial class MatchmakingSpinner : VisualElement
    {
        private static readonly string[] Frames = { "◐", "◓", "◑", "◒" };

        private readonly Label _label;
        private IVisualElementScheduledItem? _schedule;
        private int _frameIndex;
        private bool _isRunning;

        public MatchmakingSpinner()
        {
            AddToClassList("matchmaking-spinner");

            _label = new Label { name = "SpinnerLabel" };
            _label.AddToClassList("matchmaking-spinner__label");
            Add(_label);

            SetFrame(0);
        }

        public void Start()
        {
            if (_isRunning)
                return;

            _isRunning = true;
            _schedule = schedule.Execute(AdvanceFrame).Every(200);
        }

        public void Stop()
        {
            _isRunning = false;
            _schedule?.Pause();
            _schedule = null;
            SetFrame(0);
        }

        private void AdvanceFrame()
        {
            if (!_isRunning)
                return;

            _frameIndex = (_frameIndex + 1) % Frames.Length;
            SetFrame(_frameIndex);
        }

        private void SetFrame(int index)
        {
            if (index < 0 || index >= Frames.Length)
                throw new ArgumentOutOfRangeException(nameof(index), index, "Spinner frame index is out of range.");

            _label.text = Frames[index];
        }
    }
}

#nullable restore
