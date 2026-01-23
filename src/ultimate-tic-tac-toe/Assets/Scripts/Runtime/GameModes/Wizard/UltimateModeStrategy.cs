#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Runtime.GameModes.Wizard
{
    public sealed class UltimateModeStrategy : IGameModeStrategy
    {
        public const string DefaultModeId = "ultimate";

        private const string SettingsUxmlKey = "ui/mode-settings/ultimate";

        private static readonly IReadOnlyList<ValidationError> _noErrors = Array.Empty<ValidationError>();
        private static readonly ReadOnlyCollection<ValidationError> _modeConfigRequiredError =
            Array.AsReadOnly(new[] { new ValidationError(WizardFieldNames.ModeConfig, "Errors.GameModeWizard.ModeConfigRequired") });

        private static readonly ReadOnlyCollection<ValidationError> _ultimateConfigInvalidError =
            Array.AsReadOnly(new[] { new ValidationError(WizardFieldNames.ModeConfig, "Errors.GameModeWizard.UltimateConfigInvalid") });

        private readonly Func<UltimateSettingsViewModel> _createSettingsViewModel;

        public string ModeId { get; }
        public GameModeMetadata Metadata { get; }

        public UltimateModeStrategy(Func<UltimateSettingsViewModel> createSettingsViewModel)
            : this(DefaultModeId, createSettingsViewModel)
        {
        }

        public UltimateModeStrategy(string modeId, Func<UltimateSettingsViewModel> createSettingsViewModel)
        {
            if (string.IsNullOrWhiteSpace(modeId))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(modeId));
            _createSettingsViewModel = createSettingsViewModel ?? throw new ArgumentNullException(nameof(createSettingsViewModel));

            ModeId = modeId;

            Metadata = new GameModeMetadata(
                id: modeId,
                displayNameKey: "Mode.Ultimate",
                descriptionKey: "Mode.Description.Ultimate",
                iconAssetKey: "icons/game_mode_ultimate",
                sortOrder: 20,
                supportsBot: true,
                supportsOnline: true,
                supportsLocal: true);
        }

        public ModeSettingsPresentation CreatePresentation()
        {
            var vm = _createSettingsViewModel();
            if (vm == null)
                throw new InvalidOperationException("Ultimate settings VM factory returned null.");

            return new ModeSettingsPresentation(SettingsUxmlKey, vm);
        }

        public IReadOnlyList<ValidationError> ValidateConfig(IGameModeConfig? config)
        {
            if (config == null)
                return _modeConfigRequiredError;

            return config is UltimateModeConfig
                ? _noErrors
                : _ultimateConfigInvalidError;
        }
    }
}

#nullable restore
