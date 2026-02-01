using System;

namespace Runtime.UI.Core
{
    public interface IUIView
    {
        bool IsVisible { get; }
        Type ViewModelType { get; }
        
        BaseViewModel GetViewModel();
        void Show();
        void Hide();
        void Close();
        void ResetForPool();
        void InitializeFromPool();
    }

    public interface IInputBlockableView
    {
        void SetInputEnabled(bool enabled);
    }

    public interface IUIView<TViewModel> : IUIView where TViewModel : BaseViewModel
    {
        new TViewModel GetViewModel();
        void SetViewModel(TViewModel viewModel);
    }
}


