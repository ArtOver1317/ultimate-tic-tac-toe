using System;
using Runtime.UI.Core;
using UnityEngine;

namespace Runtime.Services.UI
{
    public interface IUIService
    {
        void RegisterWindowPrefab<TWindow>(GameObject prefab) where TWindow : class, IUIView;
        
        TWindow Open<TWindow, TViewModel>() 
            where TWindow : class, IUIView<TViewModel> 
            where TViewModel : BaseViewModel;
        
        TWindow Open<TWindow, TViewModel>(Action<TViewModel> configureViewModel) 
            where TWindow : class, IUIView<TViewModel> 
            where TViewModel : BaseViewModel;

        Cysharp.Threading.Tasks.UniTask<TTo> ReplaceAsync<TFrom, TTo, TToViewModel>(
            System.Threading.CancellationToken ct,
            System.Action<TToViewModel> configureViewModel = null,
            ReplaceOptions? options = null)
            where TFrom : class, IUIView
            where TTo : class, IUIView<TToViewModel>
            where TToViewModel : BaseViewModel;
        
        void Hide<TWindow>() where TWindow : IUIView;
        
        void Close<TWindow>() where TWindow : class, IUIView;
        
        void CloseAll();
        
        TWindow Get<TWindow>() where TWindow : IUIView;
        
        bool IsOpen<TWindow>() where TWindow : IUIView;
        
        void ClearViewModelPools();
        
        void ClearPools();
    }
}

