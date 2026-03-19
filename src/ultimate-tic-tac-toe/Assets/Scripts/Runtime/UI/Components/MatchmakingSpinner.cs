#nullable enable

using UnityEngine.UIElements;

namespace Runtime.UI.Components
{
    [UxmlElement]
    public sealed partial class MatchmakingSpinner : VisualElement
    {
        private const float _fullRotationDegrees = 360f;
        private const int _stepsPerRevolution = 20;
        private const float _rotationStepDegrees = _fullRotationDegrees / _stepsPerRevolution;
        private const int _frameIntervalMs = 60;

        private IVisualElementScheduledItem? _schedule;
        private float _angle;
        private bool _isRunning;

        public MatchmakingSpinner()
        {
            AddToClassList("matchmaking-spinner");
            var glyph = new Label("!");
            glyph.AddToClassList("matchmaking-spinner__glyph");
            Add(glyph);
            _angle = 0f;
            style.rotate = new Rotate(new Angle(_angle, AngleUnit.Degree));
        }

        public void Start()
        {
            if (_isRunning)
                return;

            _isRunning = true;
            
            if (_schedule == null)
                _schedule = schedule.Execute(AdvanceFrame).Every(_frameIntervalMs);
            else
                _schedule.Resume();
        }

        public void Stop()
        {
            _isRunning = false;
            _schedule?.Pause();
            _angle = 0f;
            style.rotate = new Rotate(new Angle(_angle, AngleUnit.Degree));
        }

        private void AdvanceFrame()
        {
            if (!_isRunning)
                return;

            _angle += _rotationStepDegrees;
            
            if (_angle >= _fullRotationDegrees)
                _angle -= _fullRotationDegrees;

            style.rotate = new Rotate(new Angle(_angle, AngleUnit.Degree));
        }
    }
}