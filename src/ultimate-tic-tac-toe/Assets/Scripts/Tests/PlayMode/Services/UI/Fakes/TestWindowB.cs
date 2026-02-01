using System;
using Runtime.UI.Core;

namespace Tests.PlayMode.Services.UI
{
    public sealed class TransitionTestWindowB : IUIView<TransitionTestViewModelB>, IInputBlockableView
    {
        public bool IsVisible { get; private set; }
        public Type ViewModelType => typeof(TransitionTestViewModelB);
        public int ResetForPoolCallCount { get; private set; }

        private TransitionTestViewModelB _viewModel;

        BaseViewModel IUIView.GetViewModel() => _viewModel;
        public TransitionTestViewModelB GetViewModel() => _viewModel;
        public void SetViewModel(TransitionTestViewModelB viewModel) => _viewModel = viewModel;

        public void Show() => IsVisible = true;

        public void Hide() => IsVisible = false;

        public void Close() => IsVisible = false;

        public void ResetForPool()
        {
            ResetForPoolCallCount++;
            _viewModel = null;
            IsVisible = false;
        }

        public void InitializeFromPool() { }

        public void SetInputEnabled(bool enabled) { }
    }
}
