#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Runtime.GameModes.Wizard
{
    public sealed class UltimateTicTacToeStrategy : IGameStrategy
    {
        public const string DefaultGameId = "ultimate-tic-tac-toe";

        private const string _settingsUxmlKey = "ui/mode-settings/ultimate-tic-tac-toe";

        private static readonly IReadOnlyList<ValidationError> _noErrors = Array.Empty<ValidationError>();

        private static readonly ReadOnlyCollection<ValidationError> _configRequiredError =
            Array.AsReadOnly(new[] { new ValidationError(WizardFieldNames.GameConfig, "Errors.GameWizard.ConfigRequired") });

        private static readonly ReadOnlyCollection<ValidationError> _configInvalidError =
            Array.AsReadOnly(new[] { new ValidationError(WizardFieldNames.GameConfig, "Errors.GameWizard.TicTacToeConfigInvalid") });

        private readonly Func<UltimateTicTacToeSettingsViewModel> _createSettingsViewModel;

        public string GameId { get; }
        public GameMetadata Metadata { get; }

        public UltimateTicTacToeStrategy(Func<UltimateTicTacToeSettingsViewModel> createSettingsViewModel)
            : this(DefaultGameId, createSettingsViewModel) { }

        public UltimateTicTacToeStrategy(
            string gameId,
            Func<UltimateTicTacToeSettingsViewModel> createSettingsViewModel)
        {
            if (string.IsNullOrWhiteSpace(gameId))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(gameId));

            _createSettingsViewModel = createSettingsViewModel ?? throw new ArgumentNullException(nameof(createSettingsViewModel));
            GameId = gameId;

            Metadata = new GameMetadata(
                id: gameId,
                displayNameKey: "Game.UltimateTicTacToe.Name",
                descriptionKey: "Game.UltimateTicTacToe.Description",
                iconAssetKey: "icons/game_tic_tac_toe",
                sortOrder: 11,
                supportsBot: false,
                supportsOnline: false,
                supportsLocal: true);
        }

        public GameSettingsPresentation CreatePresentation()
        {
            var vm = _createSettingsViewModel();

            if (vm == null)
                throw new InvalidOperationException("Ultimate Tic-Tac-Toe settings VM factory returned null.");

            return new GameSettingsPresentation(_settingsUxmlKey, vm);
        }

        public IReadOnlyList<ValidationError> ValidateConfig(IGameConfig? config)
        {
            if (config == null)
                return _configRequiredError;

            return config is UltimateTicTacToeConfig
                ? _noErrors
                : _configInvalidError;
        }
    }
}
