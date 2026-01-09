using System;
using Runtime.UI.Core;

namespace Tests.EditMode.Fakes
{
    public class TestViewModel : BaseViewModel 
    { 
        public new void RequestClose() => base.RequestClose();
    }

    public class AnotherTestViewModel : BaseViewModel 
    {
        public new void RequestClose() => base.RequestClose();
    }

    public class TestWindow : IUIView<TestViewModel>
    {
        public bool IsVisible { get; private set; }
        public Type ViewModelType => typeof(TestViewModel);
        public int ShowCallCount { get; set; }
        public int HideCallCount { get; private set; }

        private TestViewModel _viewModel;

        BaseViewModel IUIView.GetViewModel() => _viewModel;
        public TestViewModel GetViewModel() => _viewModel;

        public void SetViewModel(TestViewModel viewModel) => _viewModel = viewModel;

        public void Show()
        {
            IsVisible = true;
            ShowCallCount++;
        }

        public void Hide()
        {
            IsVisible = false;
            HideCallCount++;
        }

        public void Close() => IsVisible = false;

        public void ResetForPool()
        {
            _viewModel = null;
            IsVisible = false;
            ShowCallCount = 0;
            HideCallCount = 0;
        }

        public void InitializeFromPool() { }
    }

    public class AnotherTestWindow : IUIView<AnotherTestViewModel>
    {
        public bool IsVisible { get; private set; }
        public Type ViewModelType => typeof(AnotherTestViewModel);

        private AnotherTestViewModel _viewModel;

        BaseViewModel IUIView.GetViewModel() => _viewModel;
        public AnotherTestViewModel GetViewModel() => _viewModel;

        public void SetViewModel(AnotherTestViewModel viewModel) => _viewModel = viewModel;

        public void Show() => IsVisible = true;

        public void Hide() => IsVisible = false;

        public void Close() => IsVisible = false;

        public void ResetForPool()
        {
            _viewModel = null;
            IsVisible = false;
        }

        public void InitializeFromPool() { }
    }
}

