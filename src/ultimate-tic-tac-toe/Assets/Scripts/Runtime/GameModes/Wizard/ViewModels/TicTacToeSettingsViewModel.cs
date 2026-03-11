#nullable enable

using System;
using R3;
using Runtime.GameModes.Wizard.Modes;
using Runtime.UI.Core;

namespace Runtime.GameModes.Wizard.ViewModels
{
    public sealed class TicTacToeSettingsViewModel : BaseViewModel, IGameSettingsViewModel
    {
        private readonly ReactiveProperty<int> _boardSize = new(3);
        private readonly ReactiveProperty<IGameConfig> _config;
        private readonly ReactiveProperty<bool> _isValid = new(true);

        private IDisposable? _boardSizeSubscription;

        private int _minBoardSize = 3;
        private int _maxBoardSize = 10;

        public ReadOnlyReactiveProperty<int> BoardSize => _boardSize;
        public ReadOnlyReactiveProperty<IGameConfig> Config => _config;
        public ReadOnlyReactiveProperty<bool> IsValid => _isValid;

        public TicTacToeSettingsViewModel() =>
            _config = new ReactiveProperty<IGameConfig>(new TicTacToeConfig(_boardSize.Value));

        public override void Initialize()
        {
            base.Initialize();
            EnsureWired();
        }

        public void Configure(int minBoardSize, int maxBoardSize, int defaultBoardSize)
        {
            if (minBoardSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(minBoardSize), minBoardSize, "MinBoardSize must be positive.");

            if (maxBoardSize < minBoardSize)
                throw new ArgumentOutOfRangeException(nameof(maxBoardSize), maxBoardSize, "MaxBoardSize must be >= MinBoardSize.");

            if (defaultBoardSize < minBoardSize || defaultBoardSize > maxBoardSize)
                throw new ArgumentOutOfRangeException(nameof(defaultBoardSize), defaultBoardSize, "DefaultBoardSize must be within bounds.");

            _minBoardSize = minBoardSize;
            _maxBoardSize = maxBoardSize;

            EnsureWired();
            _boardSize.Value = defaultBoardSize;
        }

        public void IncrementBoardSize()
        {
            if (_boardSize.Value >= _maxBoardSize)
                return;

            _boardSize.Value = checked(_boardSize.Value + 1);
        }

        public void DecrementBoardSize()
        {
            if (_boardSize.Value <= _minBoardSize)
                return;

            _boardSize.Value = checked(_boardSize.Value - 1);
        }

        public bool TryApplyConfig(IGameConfig config)
        {
            if (config is not TicTacToeConfig tttConfig)
                return false;

            if (tttConfig.IsUltimate)
                return false;

            EnsureWired();
            _boardSize.Value = tttConfig.BoardSize;
            return true;
        }

        protected override void OnReset()
        {
            EnsureWired();
            RebuildConfig();
        }

        protected override void OnDispose()
        {
            _boardSizeSubscription?.Dispose();
            _boardSizeSubscription = null;

            _boardSize.Dispose();
            _config.Dispose();
            _isValid.Dispose();

            base.OnDispose();
        }

        private void EnsureWired()
        {
            if (_boardSizeSubscription != null)
                return;

            _boardSizeSubscription = _boardSize.Subscribe(_ => RebuildConfig());
        }

        private void RebuildConfig()
        {
            var size = Clamp(_boardSize.Value, _minBoardSize, _maxBoardSize);

            if (size != _boardSize.Value)
            {
                _boardSize.Value = size;
                return;
            }

            _isValid.Value = size >= _minBoardSize && size <= _maxBoardSize;
            _config.Value = new TicTacToeConfig(size);
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
                return min;

            return value > max ? max : value;
        }
    }
}
