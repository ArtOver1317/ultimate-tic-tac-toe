#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Runtime.GameModes.Wizard
{
    public sealed class TicTacToeStrategy : IGameStrategy
    {
        public const string DefaultGameId = "tic-tac-toe";

        private const int _defaultMinBoardSize = 3;
        private const int _defaultMaxBoardSize = 10;
        private const int _defaultBoardSizeValue = 3;

        private const string _settingsUxmlKey = "ui/mode-settings/tic-tac-toe";

        private static readonly IReadOnlyList<string> _supportedBotDifficultyIds = Array.AsReadOnly(new[]
        {
            "Easy",
            "Normal",
            "Hard",
        });

        private static readonly IReadOnlyList<ValidationError> _noErrors = Array.Empty<ValidationError>();

        private static readonly ReadOnlyCollection<ValidationError> _configRequiredError =
            Array.AsReadOnly(new[] { new ValidationError(WizardFieldNames.GameConfig, "Errors.GameWizard.ConfigRequired") });

        private static readonly ReadOnlyCollection<ValidationError> _configInvalidError =
            Array.AsReadOnly(new[] { new ValidationError(WizardFieldNames.GameConfig, "Errors.GameWizard.TicTacToeConfigInvalid") });

        private static readonly ReadOnlyCollection<ValidationError> _boardSizeInvalidError =
            Array.AsReadOnly(new[] { new ValidationError(nameof(TicTacToeConfig.BoardSize), "Errors.GameWizard.TicTacToeBoardSizeInvalid") });

        private readonly Func<TicTacToeSettingsViewModel> _createSettingsViewModel;
        private readonly int _minBoardSize;
        private readonly int _maxBoardSize;
        private readonly int _defaultBoardSize;

        public string GameId { get; }
        public GameMetadata Metadata { get; }

        public TicTacToeStrategy(Func<TicTacToeSettingsViewModel> createSettingsViewModel)
            : this(
                gameId: DefaultGameId,
                createSettingsViewModel: createSettingsViewModel,
                minBoardSize: _defaultMinBoardSize,
                maxBoardSize: _defaultMaxBoardSize,
                defaultBoardSize: _defaultBoardSizeValue) { }

        public TicTacToeStrategy(
            string gameId,
            Func<TicTacToeSettingsViewModel> createSettingsViewModel,
            int minBoardSize,
            int maxBoardSize,
            int defaultBoardSize)
        {
            if (string.IsNullOrWhiteSpace(gameId))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(gameId));

            _createSettingsViewModel = createSettingsViewModel ?? throw new ArgumentNullException(nameof(createSettingsViewModel));

            if (minBoardSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(minBoardSize), minBoardSize, "MinBoardSize must be positive.");

            if (maxBoardSize < minBoardSize)
                throw new ArgumentOutOfRangeException(nameof(maxBoardSize), maxBoardSize, "MaxBoardSize must be >= MinBoardSize.");

            if (defaultBoardSize < minBoardSize || defaultBoardSize > maxBoardSize)
                throw new ArgumentOutOfRangeException(nameof(defaultBoardSize), defaultBoardSize, "DefaultBoardSize must be within bounds.");

            GameId = gameId;
            _minBoardSize = minBoardSize;
            _maxBoardSize = maxBoardSize;
            _defaultBoardSize = defaultBoardSize;

            Metadata = new GameMetadata(
                id: gameId,
                displayNameKey: "Game.TicTacToe",
                descriptionKey: "Game.Description.TicTacToe",
                iconAssetKey: "icons/game_tic_tac_toe",
                sortOrder: 10,
                supportsBot: true,
                supportsOnline: true,
                supportsLocal: true);
        }

        public GameSettingsPresentation CreatePresentation()
        {
            var vm = _createSettingsViewModel();

            if (vm == null)
                throw new InvalidOperationException("TicTacToe settings VM factory returned null.");

            vm.Configure(_minBoardSize, _maxBoardSize, _defaultBoardSize);
            return new GameSettingsPresentation(_settingsUxmlKey, vm);
        }

        public IReadOnlyList<ValidationError> ValidateConfig(IGameConfig? config)
        {
            if (config == null)
                return _configRequiredError;

            if (config is not TicTacToeConfig tttConfig)
                return _configInvalidError;

            if (tttConfig.IsUltimate)
                return _configInvalidError;

            if (tttConfig.BoardSize < _minBoardSize || tttConfig.BoardSize > _maxBoardSize)
                return _boardSizeInvalidError;

            return _noErrors;
        }

        public IEnumerable<string> GetSupportedBotDifficultyIds() => _supportedBotDifficultyIds;
    }
}
