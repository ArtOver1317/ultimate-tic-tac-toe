#nullable enable

using System;
using R3;
using Runtime.UI.Core;

namespace Runtime.GameModes.Wizard
{
    public sealed class ClassicSettingsViewModel : BaseViewModel, ISpecificModeSettingsViewModel
    {
        private readonly ReactiveProperty<int> _boardSize = new(3);
        private readonly ReactiveProperty<IGameModeConfig> _config;
        private readonly ReactiveProperty<bool> _isValid = new(true);

        private IDisposable? _boardSizeSubscription;

        private int _minBoardSize = 3;
        private int _maxBoardSize = 10;

        public ReadOnlyReactiveProperty<int> BoardSize => _boardSize;
        public ReadOnlyReactiveProperty<IGameModeConfig> Config => _config;
        public ReadOnlyReactiveProperty<bool> IsValid => _isValid;

        public ClassicSettingsViewModel() =>
            _config = new ReactiveProperty<IGameModeConfig>(new ClassicModeConfig(_boardSize.Value));

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

            // Ensure derived state is consistent even before Initialize() is called by BaseView.
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

        public bool TryApplyConfig(IGameModeConfig config)
        {
            if (config is not ClassicModeConfig classic)
                return false;

            EnsureWired();
            _boardSize.Value = classic.BoardSize;
            return true;
        }

        protected override void OnReset()
        {
            // BaseViewModel.Reset() clears its CompositeDisposable.
            // This VM relies on an internal subscription, so re-wire it after reset.
            EnsureWired();
            ApplyBoardSize(_boardSize.Value);
        }

        protected override void OnDispose()
        {
            _boardSizeSubscription?.Dispose();
            _boardSizeSubscription = null;

            // Dispose reactive properties after un-subscribing to avoid "dispose after disposed" issues.
            _boardSize.Dispose();
            _config.Dispose();
            _isValid.Dispose();

            base.OnDispose();
        }

        private void EnsureWired()
        {
            if (_boardSizeSubscription != null)
                return;

            _boardSizeSubscription = _boardSize.Subscribe(ApplyBoardSize);
        }

        private void ApplyBoardSize(int size)
        {
            var clamped = Clamp(size, _minBoardSize, _maxBoardSize);

            // Keep the source property consistent with the bounds.
            if (clamped != size)
            {
                _boardSize.Value = clamped;
                return;
            }

            _isValid.Value = size >= _minBoardSize && size <= _maxBoardSize;
            _config.Value = new ClassicModeConfig(size);
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) 
                return min;
            
            return value > max ? max : value;
        }
    }
}