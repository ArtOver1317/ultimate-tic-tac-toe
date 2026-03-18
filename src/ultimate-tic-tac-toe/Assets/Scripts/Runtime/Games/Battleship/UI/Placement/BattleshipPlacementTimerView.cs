#nullable enable

using System;
using R3;
using Runtime.Gameplay;
using Runtime.Games.Battleship.Placement;
using Runtime.Infrastructure.Logging;
using UnityEngine.UIElements;

namespace Runtime.Games.Battleship.UI.Placement
{
    public interface IBattleshipPlacementTimerHudViewModel : IDisposable
    {
        ReadOnlyReactiveProperty<bool> IsVisible { get; }
        ReadOnlyReactiveProperty<string> FormattedTime { get; }
        ReadOnlyReactiveProperty<bool> IsWarning { get; }
    }

    public sealed class BattleshipPlacementTimerHudViewModel : IBattleshipPlacementTimerHudViewModel
    {
        private readonly IBattleshipPlacementTimerService _timerService;
        private readonly ReactiveProperty<bool> _isVisible = new(false);
        private readonly ReactiveProperty<string> _formattedTime = new("00");
        private readonly ReactiveProperty<bool> _isWarning = new(false);
        private readonly CompositeDisposable _subscriptions = new();
        private bool _disposed;
        private int _lastDisplaySeconds = int.MinValue;
        private bool _lastVisible;
        private bool _lastWarning;

        public ReadOnlyReactiveProperty<bool> IsVisible => _isVisible;
        public ReadOnlyReactiveProperty<string> FormattedTime => _formattedTime;
        public ReadOnlyReactiveProperty<bool> IsWarning => _isWarning;

        public BattleshipPlacementTimerHudViewModel(IBattleshipPlacementTimerService timerService)
        {
            _timerService = timerService ?? throw new ArgumentNullException(nameof(timerService));

            _timerService.IsActive
                .Subscribe(_ => UpdateState())
                .AddTo(_subscriptions);

            _timerService.RemainingSeconds
                .Subscribe(_ => UpdateState())
                .AddTo(_subscriptions);

            UpdateState();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _subscriptions.Dispose();
            _isVisible.Dispose();
            _formattedTime.Dispose();
            _isWarning.Dispose();
        }

        private void UpdateState()
        {
            var isVisible = _timerService.IsActive.CurrentValue;
            var remainingSeconds = _timerService.RemainingSeconds.CurrentValue;
            var displaySeconds = MoveTimerDisplayFormatter.NormalizeDisplaySeconds(remainingSeconds);
            var isWarning = isVisible && displaySeconds <= MoveTimerConstants.WarningThresholdSeconds;

            if (_lastVisible != isVisible)
            {
                _lastVisible = isVisible;
                _isVisible.Value = isVisible;
            }

            if (_lastDisplaySeconds != displaySeconds)
            {
                _lastDisplaySeconds = displaySeconds;
                _formattedTime.Value = MoveTimerDisplayFormatter.FormatSeconds(displaySeconds);
            }

            if (_lastWarning != isWarning)
            {
                _lastWarning = isWarning;
                _isWarning.Value = isWarning;
            }
        }
    }

    public sealed class BattleshipPlacementTimerHudBinder : IDisposable
    {
        private const string WarningClass = "move-timer-label--warning";

        private readonly IGameplayFieldUiAdapter _ui;
        private readonly IBattleshipPlacementTimerHudViewModel _viewModel;

        private CompositeDisposable? _subscriptions;
        private Label? _timerLabel;
        private bool _isBound;
        private bool _disposed;

        public BattleshipPlacementTimerHudBinder(IGameplayFieldUiAdapter ui, IBattleshipPlacementTimerHudViewModel viewModel)
        {
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        }

        public void Bind()
        {
            ThrowIfDisposed();

            if (_isBound)
                return;

            if (!TryResolveTimerLabel())
                return;

            _subscriptions = new CompositeDisposable();
            SubscribeToViewModel();
            ApplyCurrentState();

            _isBound = true;
        }

        private bool TryResolveTimerLabel()
        {
            _timerLabel = _ui.MoveTimerLabel;
            if (_timerLabel != null)
                return true;

            GameLog.Warning("[BattleshipPlacementTimerHudBinder] Bind skipped: MoveTimerLabel is null.");
            return false;
        }

        private void SubscribeToViewModel()
        {
            _viewModel.FormattedTime
                .Subscribe(UpdateText)
                .AddTo(_subscriptions!);

            _viewModel.IsVisible
                .Subscribe(UpdateVisibility)
                .AddTo(_subscriptions!);

            _viewModel.IsWarning
                .Subscribe(UpdateWarningStyle)
                .AddTo(_subscriptions!);
        }

        private void ApplyCurrentState()
        {
            UpdateText(_viewModel.FormattedTime.CurrentValue);
            UpdateVisibility(_viewModel.IsVisible.CurrentValue);
            UpdateWarningStyle(_viewModel.IsWarning.CurrentValue);
        }

        public void Unbind()
        {
            if (!_isBound)
                return;

            _subscriptions?.Dispose();
            _subscriptions = null;

            if (_timerLabel != null)
            {
                _timerLabel.RemoveFromClassList(WarningClass);
                _timerLabel.style.display = DisplayStyle.None;
                _timerLabel.text = "00";
            }

            _timerLabel = null;
            _isBound = false;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Unbind();
        }

        private void UpdateText(string text)
        {
            if (_timerLabel == null)
                return;

            _timerLabel.text = string.IsNullOrEmpty(text) ? "00" : text;
        }

        private void UpdateVisibility(bool isVisible)
        {
            if (_timerLabel == null)
                return;

            _timerLabel.style.display = isVisible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void UpdateWarningStyle(bool isWarning)
        {
            if (_timerLabel == null)
                return;

            if (isWarning)
                _timerLabel.AddToClassList(WarningClass);
            else
                _timerLabel.RemoveFromClassList(WarningClass);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(BattleshipPlacementTimerHudBinder));
        }
    }
}