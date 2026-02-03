using System;

namespace Runtime.Infrastructure
{
    public enum MainMenuEntryMode
    {
        Default,
        OpenWizard
    }

    public interface IMainMenuEntryModeStore
    {
        void Set(MainMenuEntryMode mode);
        bool TryConsume(out MainMenuEntryMode mode);
    }

    public sealed class MainMenuEntryModeStore : IMainMenuEntryModeStore
    {
        private MainMenuEntryMode? _mode;

        public void Set(MainMenuEntryMode mode) => _mode = mode;

        public bool TryConsume(out MainMenuEntryMode mode)
        {
            if (_mode == null)
            {
                mode = MainMenuEntryMode.Default;
                return false;
            }

            mode = _mode.Value;
            _mode = null;
            return true;
        }
    }
}
