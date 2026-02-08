#nullable enable

using UnityEngine.UIElements;

namespace Runtime.UI.Components
{
    [UxmlElement]
    public sealed partial class MatchmakingSpinner : VisualElement
    {
        private const float _rotationStep = 18f;

        private IVisualElementScheduledItem? _schedule;
        private float _angle;
        private bool _isRunning;

        public MatchmakingSpinner()
        {
            AddToClassList("matchmaking-spinner");
            _angle = 0f;
            style.rotate = new Rotate(new Angle(_angle, AngleUnit.Degree));
        }

        public void Start()
        {
            if (_isRunning)
                return;

            _isRunning = true;
            
            if (_schedule == null)
                _schedule = schedule.Execute(AdvanceFrame).Every(60);
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

            _angle += _rotationStep;
            
            if (_angle >= 360f)
                _angle -= 360f;

            style.rotate = new Rotate(new Angle(_angle, AngleUnit.Degree));
        }
    }
}