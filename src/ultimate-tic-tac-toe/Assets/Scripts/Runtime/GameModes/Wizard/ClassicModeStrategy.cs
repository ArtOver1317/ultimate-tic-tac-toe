#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Runtime.GameModes.Wizard
{
    public sealed class ClassicModeStrategy : IGameModeStrategy
    {
        public const string DefaultModeId = "classic";

        private const int DefaultMinBoardSize = 3;
        private const int DefaultMaxBoardSize = 10;
        private const int DefaultBoardSize = 3;

        private const string SettingsUxmlKey = "ui/mode-settings/classic";

        private static readonly IReadOnlyList<ValidationError> _noErrors = Array.Empty<ValidationError>();
        private static readonly ReadOnlyCollection<ValidationError> _modeConfigRequiredError =
            Array.AsReadOnly(new[] { new ValidationError("ModeConfig", "error.mode_config_required") });

        private static readonly ReadOnlyCollection<ValidationError> _classicConfigInvalidError =
            Array.AsReadOnly(new[] { new ValidationError("ModeConfig", "error.classic_config_invalid") });

        private static readonly ReadOnlyCollection<ValidationError> _classicBoardSizeInvalidError =
            Array.AsReadOnly(new[] { new ValidationError("BoardSize", "error.classic_board_size_invalid") });

        private readonly Func<ClassicSettingsViewModel> _createSettingsViewModel;
        private readonly int _minBoardSize;
        private readonly int _maxBoardSize;
        private readonly int _defaultBoardSize;

        public string ModeId { get; }
        public GameModeMetadata Metadata { get; }

        public ClassicModeStrategy(Func<ClassicSettingsViewModel> createSettingsViewModel)
            : this(
                modeId: DefaultModeId,
                createSettingsViewModel: createSettingsViewModel,
                minBoardSize: DefaultMinBoardSize,
                maxBoardSize: DefaultMaxBoardSize,
                defaultBoardSize: DefaultBoardSize)
        {
        }

        public ClassicModeStrategy(
            string modeId,
            Func<ClassicSettingsViewModel> createSettingsViewModel,
            int minBoardSize,
            int maxBoardSize,
            int defaultBoardSize)
        {
            if (string.IsNullOrWhiteSpace(modeId))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(modeId));
            _createSettingsViewModel = createSettingsViewModel ?? throw new ArgumentNullException(nameof(createSettingsViewModel));
            if (minBoardSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(minBoardSize), minBoardSize, "MinBoardSize must be positive.");
            if (maxBoardSize < minBoardSize)
                throw new ArgumentOutOfRangeException(nameof(maxBoardSize), maxBoardSize, "MaxBoardSize must be >= MinBoardSize.");
            if (defaultBoardSize < minBoardSize || defaultBoardSize > maxBoardSize)
                throw new ArgumentOutOfRangeException(nameof(defaultBoardSize), defaultBoardSize, "DefaultBoardSize must be within bounds.");

            ModeId = modeId;
            _minBoardSize = minBoardSize;
            _maxBoardSize = maxBoardSize;
            _defaultBoardSize = defaultBoardSize;

            Metadata = new GameModeMetadata(
                id: modeId,
                displayNameKey: "game_mode.classic.name",
                descriptionKey: "game_mode.classic.description",
                iconAssetKey: "icons/game_mode_classic",
                sortOrder: 10,
                supportsBot: true,
                supportsOnline: true,
                supportsLocal: true);
        }

        public ModeSettingsPresentation CreatePresentation()
        {
            var vm = _createSettingsViewModel();
            if (vm == null)
                throw new InvalidOperationException("Classic settings VM factory returned null.");

            vm.Configure(_minBoardSize, _maxBoardSize, _defaultBoardSize);
            return new ModeSettingsPresentation(SettingsUxmlKey, vm);
        }

        public IReadOnlyList<ValidationError> ValidateConfig(IGameModeConfig? config)
        {
            if (config == null)
                return _modeConfigRequiredError;

            if (config is not ClassicModeConfig classic)
                return _classicConfigInvalidError;

            if (classic.BoardSize < _minBoardSize || classic.BoardSize > _maxBoardSize)
                return _classicBoardSizeInvalidError;

            return _noErrors;
        }
    }
}

#nullable restore
