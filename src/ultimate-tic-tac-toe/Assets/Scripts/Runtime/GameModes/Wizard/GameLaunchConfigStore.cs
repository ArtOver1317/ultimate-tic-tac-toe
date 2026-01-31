using System;

namespace Runtime.GameModes.Wizard
{
    /// <summary>
    /// Thread-safe store for passing <see cref="GameLaunchConfig"/> across scene loads.
    /// </summary>
    public sealed class GameLaunchConfigStore : IGameLaunchConfigStore
    {
        private readonly object _lock = new();
        private GameLaunchConfig? _config;

        public void Set(GameLaunchConfig config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            lock (_lock)
            {
                _config = config;
            }
        }

        public bool TryPeek(out GameLaunchConfig config)
        {
            lock (_lock)
            {
                config = _config;
                return config != null;
            }
        }

        public bool TryConsume(out GameLaunchConfig config)
        {
            lock (_lock)
            {
                config = _config;
                if (config == null)
                    return false;

                _config = null;
                return true;
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _config = null;
            }
        }
    }
}
