#nullable enable

namespace Runtime.GameModes.Wizard.ViewModels
{
    /// <summary>
    /// Atomic pair: UXML addressable key + view model for the mode-specific settings section.
    /// </summary>
    public sealed class GameSettingsPresentation
    {
        public string UxmlAssetKey { get; }
        public IGameSettingsViewModel ViewModel { get; }

        public GameSettingsPresentation(string uxmlAssetKey, IGameSettingsViewModel viewModel)
        {
            if (string.IsNullOrWhiteSpace(uxmlAssetKey))
                throw new System.ArgumentException("Value cannot be null or whitespace.", nameof(uxmlAssetKey));
            
            ViewModel = viewModel ?? throw new System.ArgumentNullException(nameof(viewModel));

            UxmlAssetKey = uxmlAssetKey;
        }
    }
}