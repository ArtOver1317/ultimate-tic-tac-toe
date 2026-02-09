#nullable enable

using System;
using R3;
using Runtime.UI.Core;

namespace Runtime.GameModes.Wizard
{
    public sealed class TicTacToeSettingsViewModel : BaseViewModel, IGameSettingsViewModel
    {
        private const int _ultimateBoardSize = 3;

        private readonly ReactiveProperty<int> _boardSize = new(3);
        private readonly ReactiveProperty<bool> _isUltimate = new(false);
        private readonly ReactiveProperty<IGameConfig> _config;
        private readonly ReactiveProperty<bool> _isValid = new(true);

        private IDisposable? _boardSizeSubscription;
        private IDisposable? _isUltimateSubscription;

        private int _minBoardSize = 3;
        private int _maxBoardSize = 10;

        public ReadOnlyReactiveProperty<int> BoardSize => _boardSize;
        public ReadOnlyReactiveProperty<bool> IsUltimate => _isUltimate;
        public ReadOnlyReactiveProperty<IGameConfig> Config => _config;
        public ReadOnlyReactiveProperty<bool> IsValid => _isValid;

        public TicTacToeSettingsViewModel() =>
            _config = new ReactiveProperty<IGameConfig>(new TicTacToeConfig(_boardSize.Value, _isUltimate.Value));

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

        public void SetIsUltimate(bool isUltimate)
        {
            EnsureWired();
            _isUltimate.Value = isUltimate;
        }

        public void IncrementBoardSize()
        {
            if (_isUltimate.Value || _boardSize.Value >= _maxBoardSize)
                return;

            _boardSize.Value = checked(_boardSize.Value + 1);
        }

        public void DecrementBoardSize()
        {
            if (_isUltimate.Value || _boardSize.Value <= _minBoardSize)
                return;

            _boardSize.Value = checked(_boardSize.Value - 1);
        }

        public bool TryApplyConfig(IGameConfig config)
        {
            if (config is not TicTacToeConfig tttConfig)
                return false;

            EnsureWired();
            _isUltimate.Value = tttConfig.IsUltimate;
            _boardSize.Value = tttConfig.IsUltimate ? _ultimateBoardSize : tttConfig.BoardSize;
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
            _isUltimateSubscription?.Dispose();
            _isUltimateSubscription = null;

            _boardSize.Dispose();
            _isUltimate.Dispose();
            _config.Dispose();
            _isValid.Dispose();

            base.OnDispose();
        }

        private void EnsureWired()
        {
            if (_boardSizeSubscription != null)
                return;

            _boardSizeSubscription = _boardSize.Subscribe(_ => RebuildConfig());
            _isUltimateSubscription = _isUltimate.Subscribe(_ => RebuildConfig());
        }

        private void RebuildConfig()
        {
            var isUltimate = _isUltimate.Value;
            var size = isUltimate ? _ultimateBoardSize : Clamp(_boardSize.Value, _minBoardSize, _maxBoardSize);

            if (!isUltimate && size != _boardSize.Value)
            {
                _boardSize.Value = size;
                return;
            }

            _isValid.Value = isUltimate || (size >= _minBoardSize && size <= _maxBoardSize);
            _config.Value = new TicTacToeConfig(size, isUltimate);
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
                return min;

            return value > max ? max : value;
        }
    }
}
