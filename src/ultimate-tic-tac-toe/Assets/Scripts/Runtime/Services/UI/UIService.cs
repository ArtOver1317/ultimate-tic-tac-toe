using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using R3;
using Runtime.Infrastructure.Logging;
using Runtime.UI.Core;
using StripLog;
using UnityEngine;

namespace Runtime.Services.UI
{
    public class UIService : IUIService, IDisposable
    {
        private readonly ViewModelFactory _viewModelFactory;
        private readonly UIPoolManager _poolManager;
        private readonly Dictionary<Type, ActiveWindowEntry> _activeWindowEntries = new();
        private readonly Dictionary<Type, GameObject> _windowPrefabs = new();
        private readonly SemaphoreSlim _replaceGate = new(1, 1);

        public UIService(UIPoolManager poolManager, ViewModelFactory viewModelFactory)
        {
            _poolManager = poolManager ?? throw new ArgumentNullException(nameof(poolManager));
            _viewModelFactory = viewModelFactory ?? throw new ArgumentNullException(nameof(viewModelFactory));
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

            if (TryGetActiveEntry(windowType, out var existingEntry))
            {
                var typedWindow = (TWindow)existingEntry.Window;
                typedWindow.Show();
                Log.Debug(LogTags.Services, $"[UIService] Showing existing window: {windowType.Name}");
                return typedWindow;
            }

            if (!_windowPrefabs.TryGetValue(windowType, out var prefab))
                throw new InvalidOperationException($"[UIService] Window {windowType.Name} prefab not registered.");

            return CreateWindowFromPrefab<TWindow, TViewModel>(prefab);
        }

        public TWindow Open<TWindow, TViewModel>(Action<TViewModel> configureViewModel) 
            where TWindow : class, IUIView<TViewModel> 
            where TViewModel : BaseViewModel
        {
            var window = Open<TWindow, TViewModel>();

            var viewModel = window.GetViewModel();
            configureViewModel?.Invoke(viewModel);
            
            return window;
        }

        public async Cysharp.Threading.Tasks.UniTask<TTo> ReplaceAsync<TFrom, TTo, TToViewModel>(
            CancellationToken ct,
            Action<TToViewModel> configureViewModel = null,
            ReplaceOptions? options = null)
            where TFrom : class, IUIView
            where TTo : class, IUIView<TToViewModel>
            where TToViewModel : BaseViewModel
        {
            await _replaceGate.WaitAsync(ct);

            try
            {
                await Cysharp.Threading.Tasks.UniTask.SwitchToMainThread(ct);

                var effective = options ?? new ReplaceOptions(
                    keepFromVisibleUntilToShown: true,
                    disableFromInputImmediately: true,
                    closeFromAfterToOpened: true);
                
                var from = Get<TFrom>();

                if (from is IInputBlockableView inputBlockable && effective.DisableFromInputImmediately)
                    inputBlockable.SetInputEnabled(false);

                if (!effective.KeepFromVisibleUntilToShown)
                    from?.Hide();

                try
                {
                    var to = configureViewModel == null
                        ? Open<TTo, TToViewModel>()
                        : Open<TTo, TToViewModel>(configureViewModel);

                    if (effective.CloseFromAfterToOpened && from != null)
                        Close<TFrom>();

                    return to;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    if (from is IInputBlockableView rollback)
                        rollback.SetInputEnabled(true);

                    throw;
                }
            }
            finally
            {
                _replaceGate.Release();
            }
        }

        public void Hide<TWindow>() where TWindow : IUIView
        {
            var windowType = typeof(TWindow);
            
            if (TryGetActiveEntry(windowType, out var entry))
            {
                entry.Window.Hide();
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
            var windowTypes = _activeWindowEntries.Keys.ToList();
            
            foreach (var windowType in windowTypes)
            {
                TryCloseWindow(windowType);
            }
            
            Log.Debug(LogTags.Services, "[UIService] Closed all windows");
        }

        public TWindow Get<TWindow>() where TWindow : IUIView
        {
            var windowType = typeof(TWindow);
            return TryGetActiveEntry(windowType, out var entry) ? (TWindow)entry.Window : default;
        }

        public bool IsOpen<TWindow>() where TWindow : IUIView
        {
            var windowType = typeof(TWindow);
            return TryGetActiveEntry(windowType, out var entry) && entry.Window.IsVisible;
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
                throw new InvalidOperationException($"[UIService] Failed to get or instantiate window: {windowType.Name}.");

            var viewModelType = typeof(TViewModel);
            var viewModel = _poolManager.GetViewModelFromPool<TViewModel>(viewModelType) ?? CreateViewModel<TViewModel>();
            window.SetViewModel(viewModel);
            _activeWindowEntries[windowType] = CreateActiveWindowEntry(windowType, window, viewModel);
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
            if (!_activeWindowEntries.Remove(windowType, out var entry))
                return false;

            entry.DisposeCloseSubscription();
            _poolManager.ReturnWindowToPool(entry.Window);

            if (entry.ViewModel != null)
                _poolManager.ReturnViewModelToPool(entry.ViewModel);
            
            return true;
        }

        private bool TryGetActiveEntry(Type windowType, out ActiveWindowEntry entry) =>
            _activeWindowEntries.TryGetValue(windowType, out entry);

        private ActiveWindowEntry CreateActiveWindowEntry(Type windowType, IUIView window, BaseViewModel viewModel)
        {
            var closeSubscription = viewModel.OnCloseRequested
                .Subscribe(_ =>
                {
                    Log.Debug(LogTags.Services, $"[UIService] Close requested for window: {windowType.Name}");
                    CloseWindowByType(windowType);
                });

            return new ActiveWindowEntry(window, viewModel, closeSubscription);
        }

        private sealed class ActiveWindowEntry
        {
            public ActiveWindowEntry(IUIView window, BaseViewModel viewModel, IDisposable closeSubscription)
            {
                Window = window;
                ViewModel = viewModel;
                CloseSubscription = closeSubscription;
            }

            public IUIView Window { get; }

            public BaseViewModel ViewModel { get; }

            private IDisposable CloseSubscription { get; }

            public void DisposeCloseSubscription() => CloseSubscription?.Dispose();
        }
    }
}