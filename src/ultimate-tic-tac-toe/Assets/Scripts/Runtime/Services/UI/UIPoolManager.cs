using System;
using System.Collections.Generic;
using Runtime.Infrastructure.Logging;
using Runtime.UI.Core;
using StripLog;
using UnityEngine;
using VContainer;

namespace Runtime.Services.UI
{
    public class UIPoolManager
    {
        private readonly IObjectResolver _container;
        private readonly IObjectPool<IUIView> _windowPool;
        
        private readonly IObjectPool<BaseViewModel> _viewModelPool;
        
        // Inject is intentionally one-time per instance lifetime (pool-safe contract).
        private readonly HashSet<int> _injectedInstanceIds = new();

        public UIPoolManager(
            IObjectResolver container,
            IObjectPool<IUIView> windowPool = null,
            IObjectPool<BaseViewModel> viewModelPool = null)
        {
            _container = container;
            _windowPool = windowPool ?? new ObjectPool<IUIView>();
            _viewModelPool = viewModelPool ?? new ObjectPool<BaseViewModel>();
        }

        public TWindow GetOrInstantiateWindow<TWindow>(Type windowType, GameObject prefab) 
            where TWindow : class, IUIView
        {
            var pooledWindow = TryReuseWindowFromPool<TWindow>(windowType);
            return pooledWindow ?? InstantiateWindow<TWindow>(windowType, prefab);
        }

        public TViewModel GetViewModelFromPool<TViewModel>(Type viewModelType) where TViewModel : BaseViewModel
        {
            var viewModel = _viewModelPool.Get<TViewModel>(viewModelType);
            return viewModel;
        }
        
        public bool ReturnWindowToPool(IUIView window)
        {
            var windowType = window.GetType();

            return _windowPool.Return(windowType, window, w =>
            {
                w.ResetForPool();
                
                if (w is MonoBehaviour mb && mb)
                    mb.gameObject.SetActive(false);
            });
        }
        
        public bool ReturnViewModelToPool(BaseViewModel viewModel)
        {
            var viewModelType = viewModel.GetType();
            return _viewModelPool.Return(viewModelType, viewModel, vm => vm.Reset());
        }

        public void ClearViewModelPools()
        {
            _viewModelPool.ClearAll(vm => vm.Dispose());
            Log.Debug(LogTags.Services, "[UIPoolManager] ViewModel pools cleared");
        }

        public void ClearAllPools()
        {
            _windowPool.ClearAll(CleanupPooledWindow);
            
            _viewModelPool.ClearAll(vm => vm.Dispose());
            Log.Debug(LogTags.Services, "[UIPoolManager] All pools cleared");
            _injectedInstanceIds.Clear();
        }

        public void ClearPool(Type windowType)
        {
            _windowPool.Clear(windowType, CleanupPooledWindow);
            
            Log.Debug(LogTags.Services, $"[UIPoolManager] Cleared pool for {windowType.Name}");
        }

        private TWindow TryReuseWindowFromPool<TWindow>(Type windowType) where TWindow : class, IUIView
        {
            var pooledWindow = _windowPool.Get<TWindow>(windowType);

            if (pooledWindow == null)
                return null;

            ReactivateAndInjectWindow(pooledWindow);
            Log.Debug(LogTags.Services, $"[UIPoolManager] Retrieved window from pool: {windowType.Name}");
            return pooledWindow;
        }

        private TWindow InstantiateWindow<TWindow>(Type windowType, GameObject prefab) where TWindow : class, IUIView
        {
            var instance = UnityEngine.Object.Instantiate(prefab);
            UnityEngine.Object.DontDestroyOnLoad(instance);

            var window = instance.GetComponent<TWindow>();

            if (window == null)
            {
                Log.Error(LogTags.Services, $"[UIPoolManager] Prefab doesn't have {windowType.Name} component!");
                UnityEngine.Object.Destroy(instance);
                return null;
            }

            EnsureWindowInjected(window);
            Log.Debug(LogTags.Services, $"[UIPoolManager] Created new window instance: {windowType.Name}");
            return window;
        }

        private void ReactivateAndInjectWindow(IUIView window)
        {
            if (window is MonoBehaviour monoBehaviour)
                monoBehaviour.gameObject.SetActive(true);

            EnsureWindowInjected(window);
        }

        private void EnsureWindowInjected(IUIView window)
        {
            if (window is MonoBehaviour monoBehaviour)
            {
                EnsureInjected(monoBehaviour.gameObject);
                EnsureInjected(monoBehaviour);
                return;
            }

            EnsureInjected(window);
        }

        private void CleanupPooledWindow(IUIView window)
        {
            RemoveInjectedId(window);

            try
            {
                window.Close();
            }
            catch (MissingReferenceException)
            {
                // View is already destroyed; ignore.
            }

            if (window is MonoBehaviour monoBehaviour && monoBehaviour)
                UnityEngine.Object.Destroy(monoBehaviour.gameObject);
        }

        private void EnsureInjected(object target)
        {
            if (target is UnityEngine.Object unityObject)
            {
                var id = unityObject.GetInstanceID();
                
                if (!_injectedInstanceIds.Add(id))
                    return;
            }

            _container.Inject(target);
        }

        private void RemoveInjectedId(IUIView view)
        {
            if (view is not MonoBehaviour mb)
                return;

            if (!mb)
                return;

            var go = mb.gameObject;
            
            if (!go)
                return;

            _injectedInstanceIds.Remove(go.GetInstanceID());
            _injectedInstanceIds.Remove(mb.GetInstanceID());
        }

        public int GetPoolSize(Type windowType) => _windowPool.GetSize(windowType);

        public Dictionary<Type, int> GetPoolStats() => _windowPool.GetStats();
    }
}
