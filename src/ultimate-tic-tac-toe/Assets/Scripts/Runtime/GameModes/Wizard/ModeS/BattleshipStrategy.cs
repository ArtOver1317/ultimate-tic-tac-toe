#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Runtime.GameModes.Wizard
{
    public sealed class BattleshipStrategy : IGameStrategy, IGameStartValidator
    {
        public const string DefaultGameId = "battleship";
        public const string DefaultBotDifficultyId = "Easy";

        private const string _settingsUxmlKey = "ui/mode-settings/battleship";

        private static readonly IReadOnlyList<string> _supportedBotDifficultyIds = Array.AsReadOnly(new[]
        {
            DefaultBotDifficultyId,
        });

        private static readonly IReadOnlyList<ValidationError> _noErrors = Array.Empty<ValidationError>();

        private static readonly ReadOnlyCollection<ValidationError> _configRequiredError =
            Array.AsReadOnly(new[] { new ValidationError(WizardFieldNames.GameConfig, "Errors.GameWizard.ConfigRequired") });

        private static readonly ReadOnlyCollection<ValidationError> _configInvalidError =
            Array.AsReadOnly(new[] { new ValidationError(WizardFieldNames.GameConfig, "Errors.GameWizard.BattleshipConfigInvalid") });

        private static readonly ReadOnlyCollection<ValidationError> _onlineMoveTimerRequiredError =
            Array.AsReadOnly(new[] { new ValidationError(WizardFieldNames.Matchmaking, "Errors.GameWizard.BattleshipMoveTimerRequired") });

        private static readonly ReadOnlyCollection<ValidationError> _onlinePlacementTimerRequiredError =
            Array.AsReadOnly(new[] { new ValidationError(WizardFieldNames.GameConfig, "Errors.GameWizard.BattleshipPlacementTimerRequired") });

        private static readonly ReadOnlyCollection<ValidationError> _localHumanNotSupportedError =
            Array.AsReadOnly(new[] { new ValidationError(WizardFieldNames.Matchmaking, "Errors.GameWizard.BattleshipLocalHumanUnsupported") });

        private readonly Func<BattleshipSettingsViewModel> _createSettingsViewModel;

        public string GameId { get; }
        public GameMetadata Metadata { get; }

        public BattleshipStrategy(Func<BattleshipSettingsViewModel> createSettingsViewModel)
            : this(DefaultGameId, createSettingsViewModel) { }

        public BattleshipStrategy(string gameId, Func<BattleshipSettingsViewModel> createSettingsViewModel)
        {
            if (string.IsNullOrWhiteSpace(gameId))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(gameId));

            _createSettingsViewModel = createSettingsViewModel ?? throw new ArgumentNullException(nameof(createSettingsViewModel));
            GameId = gameId;

            Metadata = new GameMetadata(
                id: gameId,
                displayNameKey: "Game.Battleship.Name",
                descriptionKey: "Game.Battleship.Description",
                iconAssetKey: "icons/game_battleship",
                sortOrder: 20,
                supportsBot: true,
                supportsOnline: true,
                supportsLocal: false);
        }

        public GameSettingsPresentation CreatePresentation()
        {
            var vm = _createSettingsViewModel();

            if (vm == null)
                throw new InvalidOperationException("Battleship settings VM factory returned null.");

            return new GameSettingsPresentation(_settingsUxmlKey, vm);
        }

        public IReadOnlyList<ValidationError> ValidateConfig(IGameConfig? config)
        {
            if (config == null)
                return _configRequiredError;

            if (config is not BattleshipConfig)
                return _configInvalidError;

            return _noErrors;
        }

        public IReadOnlyList<ValidationError> ValidateForStart(GameSessionSnapshot snapshot)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));

            if (snapshot.OpponentType == OpponentType.Human && snapshot.HumanOpponentKind == HumanOpponentKind.Local)
                return _localHumanNotSupportedError;

            if (snapshot.OpponentType != OpponentType.Human)
                return _noErrors;

            if (snapshot.MoveTimeLimitSeconds <= 0)
                return _onlineMoveTimerRequiredError;

            if (snapshot.GameConfig is not BattleshipConfig battleshipConfig)
                return _noErrors;

            return battleshipConfig.PlacementTimeLimitSeconds > 0
                ? _noErrors
                : _onlinePlacementTimerRequiredError;
        }

        public IEnumerable<string> GetSupportedBotDifficultyIds() => _supportedBotDifficultyIds;
    }
}
