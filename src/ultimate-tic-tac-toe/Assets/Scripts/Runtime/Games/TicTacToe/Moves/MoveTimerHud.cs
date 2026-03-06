using System;
using R3;
using Runtime.Gameplay;
using Runtime.Infrastructure.Logging;
using UnityEngine.UIElements;

namespace Runtime.Games.TicTacToe.Moves
{
    public interface IMoveTimerHudViewModel : IDisposable
    {
        ReadOnlyReactiveProperty<bool> IsVisible { get; }
        ReadOnlyReactiveProperty<string> FormattedTime { get; }
        ReadOnlyReactiveProperty<bool> IsWarning { get; }
    }

    public sealed class MoveTimerHudViewModel : IMoveTimerHudViewModel
    {
        private readonly IMoveTimerService _moveTimerService;
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

        public MoveTimerHudViewModel(IMoveTimerService moveTimerService)
        {
            _moveTimerService = moveTimerService ?? throw new ArgumentNullException(nameof(moveTimerService));

            _moveTimerService.IsActive
                .Subscribe(_ => UpdateState())
                .AddTo(_subscriptions);

            _moveTimerService.RemainingSeconds
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
            var isVisible = _moveTimerService.IsActive.CurrentValue;
            var remainingSeconds = _moveTimerService.RemainingSeconds.CurrentValue;
            var displaySeconds = NormalizeDisplaySeconds(remainingSeconds);
            var isWarning = isVisible && displaySeconds <= MoveTimerConstants.WarningThresholdSeconds;

            if (_lastVisible != isVisible)
            {
                _lastVisible = isVisible;
                _isVisible.Value = isVisible;
            }

            if (_lastDisplaySeconds != displaySeconds)
            {
                _lastDisplaySeconds = displaySeconds;
                _formattedTime.Value = FormatSeconds(displaySeconds);
            }

            if (_lastWarning != isWarning)
            {
                _lastWarning = isWarning;
                _isWarning.Value = isWarning;
            }
        }

        private static int NormalizeDisplaySeconds(float remainingSeconds)
        {
            var ceil = (int)Math.Ceiling(remainingSeconds);
            return ceil > 0 ? ceil : 0;
        }

        private static string FormatSeconds(int totalSeconds)
        {
            if (totalSeconds >= 60)
            {
                var minutes = totalSeconds / 60;
                var seconds = totalSeconds % 60;
                return $"{minutes:00}:{seconds:00}";
            }

            return totalSeconds.ToString("00");
        }
    }

    public sealed class MoveTimerHudBinder : IDisposable
    {
        private const string WarningClass = "move-timer-label--warning";

        private readonly IGameplayFieldUiAdapter _ui;
        private readonly IMoveTimerHudViewModel _viewModel;

        private CompositeDisposable _subscriptions;
        private Label _timerLabel;
        private bool _isBound;
        private bool _disposed;
        private bool _lastViewModelVisibility;
        private bool? _visibilityOverride;

        public bool IsBound => _isBound;

        public MoveTimerHudBinder(IGameplayFieldUiAdapter ui, IMoveTimerHudViewModel viewModel)
        {
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        }

        public void Bind()
        {
            ThrowIfDisposed();

            if (_isBound)
            {
                GameLog.Warning("[MoveTimerHudBinder] Bind called more than once. Ignored.");
                return;
            }

            _timerLabel = _ui.MoveTimerLabel;
            if (_timerLabel == null)
            {
                GameLog.Warning("[MoveTimerHudBinder] Bind skipped: MoveTimerLabel is null.");
                return;
            }

            _subscriptions = new CompositeDisposable();

            _viewModel.FormattedTime
                .Subscribe(UpdateText)
                .AddTo(_subscriptions);

            _viewModel.IsVisible
                .Subscribe(UpdateVisibility)
                .AddTo(_subscriptions);

            _viewModel.IsWarning
                .Subscribe(UpdateWarningStyle)
                .AddTo(_subscriptions);

            UpdateText(_viewModel.FormattedTime.CurrentValue);
            UpdateVisibility(_viewModel.IsVisible.CurrentValue);
            UpdateWarningStyle(_viewModel.IsWarning.CurrentValue);

            _isBound = true;
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

            _lastViewModelVisibility = false;
            _visibilityOverride = null;
            _timerLabel = null;
            _isBound = false;
        }

        public void SetVisibilityOverride(bool? isVisible)
        {
            _visibilityOverride = isVisible;
            ApplyVisibility();
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
            _lastViewModelVisibility = isVisible;
            ApplyVisibility();
        }

        private void ApplyVisibility()
        {
            if (_timerLabel == null)
                return;

            var isVisible = _visibilityOverride ?? _lastViewModelVisibility;
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
                throw new ObjectDisposedException(nameof(MoveTimerHudBinder));
        }
    }
}