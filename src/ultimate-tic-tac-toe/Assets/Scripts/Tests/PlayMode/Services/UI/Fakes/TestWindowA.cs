using System;
using Runtime.UI.Core;

namespace Tests.PlayMode.Services.UI
{
    public sealed class TransitionTestWindowA : IUIView<TransitionTestViewModelA>, IInputBlockableView
    {
        public bool IsVisible { get; private set; }
        public Type ViewModelType => typeof(TransitionTestViewModelA);
        public int HideCallCount { get; private set; }
        public int ResetForPoolCallCount { get; private set; }
        public int SetInputEnabledCallCount { get; private set; }
        public bool InputEnabled { get; private set; } = true;

        private TransitionTestViewModelA _viewModel;

        BaseViewModel IUIView.GetViewModel() => _viewModel;
        public TransitionTestViewModelA GetViewModel() => _viewModel;
        public void SetViewModel(TransitionTestViewModelA viewModel) => _viewModel = viewModel;

        public void Show() => IsVisible = true;

        public void Hide()
        {
            IsVisible = false;
            HideCallCount++;
        }

        public void Close() => IsVisible = false;

        public void ResetForPool()
        {
            ResetForPoolCallCount++;
            _viewModel = null;
            IsVisible = false;
        }

        public void InitializeFromPool() { }

        public void SetInputEnabled(bool enabled)
        {
            InputEnabled = enabled;
            SetInputEnabledCallCount++;
        }
    }
}
