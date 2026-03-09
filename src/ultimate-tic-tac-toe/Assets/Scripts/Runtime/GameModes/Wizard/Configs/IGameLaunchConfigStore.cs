namespace Runtime.GameModes.Wizard.Configs
{
    /// <summary>
    /// Stores the latest game launch configuration between scene transitions.
    /// </summary>
    public interface IGameLaunchConfigStore
    {
        /// <summary>
        /// Store the provided launch configuration.
        /// </summary>
        void Set(GameLaunchConfig config);

        /// <summary>
        /// Try to read the stored configuration without clearing it.
        /// </summary>
        bool TryPeek(out GameLaunchConfig config);

        /// <summary>
        /// Try to read and clear the stored configuration.
        /// </summary>
        bool TryConsume(out GameLaunchConfig config);

        /// <summary>
        /// Clears the stored configuration.
        /// </summary>
        void Clear();
    }
}