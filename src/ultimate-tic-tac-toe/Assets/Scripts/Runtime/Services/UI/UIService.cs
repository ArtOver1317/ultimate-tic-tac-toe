using System;
using System.Collections.Generic;
using System.Linq;
using R3;
using Runtime.Infrastructure.Logging;
using Runtime.UI.Core;
using StripLog;
using UnityEngine;
using VContainer;

namespace Runtime.Services.UI
{
    public class UIService : IUIService, IDisposable
    {
        private readonly ViewModelFactory _viewModelFactory;
        private readonly UIPoolManager _poolManager;
        private readonly Dictionary<Type, IUIView> _activeWindows = new();
        private readonly Dictionary<Type, GameObject> _windowPrefabs = new();
        private readonly Dictionary<Type, IDisposable> _closeSubscriptions = new();

        public UIService(IObjectResolver container)
        {
            _viewModelFactory = new ViewModelFactory(container);
            _poolManager = new UIPoolManager(container);
        }

        internal UIService(IObjectResolver container, UIPoolManager poolManager, ViewModelFactory viewModelFactory)
        {
            _poolManager = poolManager;
            _viewModelFactory = viewModelFactory;
        }

        public void RegisterWindowPrefab<TWindow>(GameObject prefab) where TWindow : class, IUIView
        {
            var windowType = typeof(TWindow);
            _windowPrefabs[windowType] = prefab;
            Log.Debug(LogTags.Services, $"[UIService] Registered window prefab: {windowType.Name}");
        }

        public TWindow Open<TWindow, TViewModel>() 
            where TWindow : class, IUIView<TViewModel> 
            where TViewModel : BaseViewModel
        {
            var windowType = typeof(TWindow);

            if (_activeWindows.TryGetValue(windowType, out var existingWindow))
            {
                var typedWindow = (TWindow)existingWindow;
                typedWindow.Show();
                Log.Debug(LogTags.Services, $"[UIService] Showing existing window: {windowType.Name}");
                return typedWindow;
            }

            if (_windowPrefabs.TryGetValue(windowType, out var prefab))
                return CreateWindowFromPrefab<TWindow, TViewModel>(prefab);

            Log.Error(LogTags.Services, $"[UIService] Window {windowType.Name} prefab not registered!");
            return null;
        }

        public TWindow Open<TWindow, TViewModel>(Action<TViewModel> configureViewModel) 
            where TWindow : class, IUIView<TViewModel> 
            where TViewModel : BaseViewModel
        {
            var window = Open<TWindow, TViewModel>();
            
            if (window != null)
            {
                var viewModel = window.GetViewModel();
                configureViewModel?.Invoke(viewModel);
            }
            
            return window;
        }

        public void Hide<TWindow>() where TWindow : IUIView
        {
            var windowType = typeof(TWindow);
            
            if (_activeWindows.TryGetValue(windowType, out var window))
            {
                window.Hide();
                Log.Debug(LogTags.Services, $"[UIService] Hidden window: {windowType.Name}");
            }
        }

        public void Close<TWindow>() where TWindow : class, IUIView
        {
            var windowType = typeof(TWindow);
            
            if (TryCloseWindow(windowType))
                Log.Debug(LogTags.Services, $"[UIService] Closed window: {windowType.Name}");
        }

        public void CloseAll()
        {
            var windowTypes = _activeWindows.Keys.ToList();
            
            foreach (var windowType in windowTypes)
                TryCloseWindow(windowType);
            
            foreach (var subscription in _closeSubscriptions.Values)
                subscription?.Dispose();
            
            _closeSubscriptions.Clear();
            Log.Debug(LogTags.Services, "[UIService] Closed all windows");
        }

        public TWindow Get<TWindow>() where TWindow : IUIView
        {
            var windowType = typeof(TWindow);
            return _activeWindows.TryGetValue(windowType, out var window) ? (TWindow)window : default;
        }

        public bool IsOpen<TWindow>() where TWindow : IUIView
        {
            var windowType = typeof(TWindow);
            return _activeWindows.TryGetValue(windowType, out var window) && window.IsVisible;
        }

        public void ClearViewModelPools() => _poolManager.ClearViewModelPools();

        public void ClearPools() => _poolManager.ClearAllPools();

        public void Dispose()
        {
            CloseAll();
            ClearPools();
        }

        private TWindow CreateWindowFromPrefab<TWindow, TViewModel>(GameObject prefab)
            where TWindow : class, IUIView<TViewModel>
            where TViewModel : BaseViewModel
        {
            var windowType = typeof(TWindow);
            var window = _poolManager.GetOrInstantiateWindow<TWindow>(windowType, prefab);
            
            if (window == null)
            {
                Log.Error(LogTags.Services, $"[UIService] Failed to get or instantiate window: {windowType.Name}");
                return null;
            }

            var viewModelType = typeof(TViewModel);
            var viewModel = _poolManager.GetViewModelFromPool<TViewModel>(viewModelType) ?? CreateViewModel<TViewModel>();
            window.SetViewModel(viewModel);
            _activeWindows[windowType] = window;
            
            var closeSubscription = viewModel.OnCloseRequested
                .Subscribe(_ =>
                {
                    Log.Debug(LogTags.Services, $"[UIService] Close requested for window: {windowType.Name}");
                    CloseWindowByType(windowType);
                });
            
            _closeSubscriptions[windowType] = closeSubscription;
            window.Show();
            Log.Debug(LogTags.Services, $"[UIService] Created window from prefab: {windowType.Name}");
            return window;
        }

        private TViewModel CreateViewModel<TViewModel>() where TViewModel : BaseViewModel => 
            _viewModelFactory.CreateViewModel<TViewModel>();

        private void CloseWindowByType(Type windowType)
        {
            if (TryCloseWindow(windowType))
                Log.Debug(LogTags.Services, $"[UIService] Closed window: {windowType.Name}");
        }

        private bool TryCloseWindow(Type windowType)
        {
            if (!_activeWindows.Remove(windowType, out var window))
                return false;
            
            if (_closeSubscriptions.Remove(windowType, out var subscription))
                subscription?.Dispose();
            
            var viewModel = GetViewModelFromWindow(window);
            _poolManager.ReturnWindowToPool(window);
            
            if (viewModel != null)
                _poolManager.ReturnViewModelToPool(viewModel);
            
            return true;
        }

        private BaseViewModel GetViewModelFromWindow(IUIView window) => window.GetViewModel();
    }
}
