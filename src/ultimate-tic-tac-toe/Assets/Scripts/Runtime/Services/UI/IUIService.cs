using System;
using Runtime.UI.Core;
using UnityEngine;

namespace Runtime.Services.UI
{
    public interface IUIService
    {
        void RegisterWindowPrefab<TWindow>(GameObject prefab) where TWindow : class, IUIView;

        /// <summary>
        /// Opens an existing window or creates a new one from the registered prefab.
        /// Throws <see cref="InvalidOperationException"/> when the window prefab is not registered
        /// or the window instance cannot be created.
        /// </summary>
        TWindow Open<TWindow, TViewModel>() 
            where TWindow : class, IUIView<TViewModel> 
            where TViewModel : BaseViewModel;

        /// <summary>
        /// Opens an existing window or creates a new one from the registered prefab,
        /// then applies additional ViewModel configuration.
        /// Throws <see cref="InvalidOperationException"/> when the window prefab is not registered
        /// or the window instance cannot be created.
        /// </summary>
        TWindow Open<TWindow, TViewModel>(Action<TViewModel> configureViewModel) 
            where TWindow : class, IUIView<TViewModel> 
            where TViewModel : BaseViewModel;

        /// <summary>
        /// Replaces one window with another.
        /// Throws <see cref="InvalidOperationException"/> when the target window cannot be opened.
        /// </summary>
        Cysharp.Threading.Tasks.UniTask<TTo> ReplaceAsync<TFrom, TTo, TToViewModel>(
            System.Threading.CancellationToken ct,
            Action<TToViewModel> configureViewModel = null,
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

        /// <summary>
        /// Registers an already-visible scene window with UIService so it is tracked as an active
        /// window. Use this for windows placed directly in the scene that must be visible from the
        /// first frame. The window is NOT shown again (avoids re-triggering fade-in).
        /// </summary>
        void AdoptSceneWindow<TWindow, TViewModel>(TWindow window)
            where TWindow : class, IUIView<TViewModel>
            where TViewModel : BaseViewModel;
    }
}

