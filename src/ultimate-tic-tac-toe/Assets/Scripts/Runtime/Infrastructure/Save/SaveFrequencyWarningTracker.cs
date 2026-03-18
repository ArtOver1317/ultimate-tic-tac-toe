using System;
using Runtime.Infrastructure.Logging;

namespace Runtime.Infrastructure.Save
{
    internal sealed class SaveFrequencyWarningTracker
    {
        private const double _warningWindowSeconds = 1d;
        private const double _warningCooldownSeconds = 1d;

#if SAVE_ENCRYPTION_DISABLED || UNITY_EDITOR || DEVELOPMENT_BUILD
        private readonly int _warningThreshold;
        private DateTime _saveWindowStartedUtc = DateTime.UtcNow;
        private DateTime _lastSaveFrequencyWarningUtc = DateTime.MinValue;
        private int _saveCallsInWindow;
#endif

        public SaveFrequencyWarningTracker(int warningThreshold)
        {
#if SAVE_ENCRYPTION_DISABLED || UNITY_EDITOR || DEVELOPMENT_BUILD
            _warningThreshold = warningThreshold;
#endif
        }

        public void Track(string section, int payloadBytes)
        {
#if SAVE_ENCRYPTION_DISABLED || UNITY_EDITOR || DEVELOPMENT_BUILD
            var now = DateTime.UtcNow;

            if ((now - _saveWindowStartedUtc).TotalSeconds >= _warningWindowSeconds)
            {
                _saveWindowStartedUtc = now;
                _saveCallsInWindow = 0;
            }

            _saveCallsInWindow++;

            if (_saveCallsInWindow <= _warningThreshold)
                return;

            if ((now - _lastSaveFrequencyWarningUtc).TotalSeconds < _warningCooldownSeconds)
                return;

            _lastSaveFrequencyWarningUtc = now;
            GameLog.Warning($"[SaveSystem] Save called too frequently. Section={section}, CallsPerSecond={_saveCallsInWindow}, PayloadBytes={payloadBytes}");
#endif
        }
    }
}