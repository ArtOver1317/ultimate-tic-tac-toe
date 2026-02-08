#nullable enable

namespace Runtime.GameModes.Wizard
{
    /// <summary>
    /// Atomic pair: UXML addressable key + view model for the mode-specific settings section.
    /// </summary>
    public sealed class ModeSettingsPresentation
    {
        public string UxmlAssetKey { get; }
        public ISpecificModeSettingsViewModel ViewModel { get; }

        public ModeSettingsPresentation(string uxmlAssetKey, ISpecificModeSettingsViewModel viewModel)
        {
            if (string.IsNullOrWhiteSpace(uxmlAssetKey))
                throw new System.ArgumentException("Value cannot be null or whitespace.", nameof(uxmlAssetKey));
            
            ViewModel = viewModel ?? throw new System.ArgumentNullException(nameof(viewModel));

            UxmlAssetKey = uxmlAssetKey;
        }
    }
}