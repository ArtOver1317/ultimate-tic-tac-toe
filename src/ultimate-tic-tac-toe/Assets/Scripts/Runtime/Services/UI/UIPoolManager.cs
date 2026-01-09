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
            var pooledWindow = _windowPool.Get<TWindow>(windowType);
            
            if (pooledWindow != null)
            {
                if (pooledWindow is MonoBehaviour mb) 
                    mb.gameObject.SetActive(true);

                Log.Debug(LogTags.Services, $"[UIPoolManager] Retrieved window from pool: {windowType.Name}");
                return pooledWindow;
            }

            var instance = UnityEngine.Object.Instantiate(prefab);
            UnityEngine.Object.DontDestroyOnLoad(instance);
            _container.Inject(instance);
            var window = instance.GetComponent<TWindow>();
            
            if (window == null)
            {
                Log.Error(LogTags.Services, $"[UIPoolManager] Prefab doesn't have {windowType.Name} component!");
                UnityEngine.Object.Destroy(instance);
                return null;
            }

            Log.Debug(LogTags.Services, $"[UIPoolManager] Created new window instance: {windowType.Name}");
            return window;
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
                
                if (w is MonoBehaviour mb) 
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
            _windowPool.ClearAll(w =>
            {
                w.Close();
                
                if (w is MonoBehaviour mb) 
                    UnityEngine.Object.Destroy(mb.gameObject);
            });
            
            _viewModelPool.ClearAll(vm => vm.Dispose());
            Log.Debug(LogTags.Services, "[UIPoolManager] All pools cleared");
        }

        public void ClearPool(Type windowType)
        {
            _windowPool.Clear(windowType, w =>
            {
                w.Close();
                
                if (w is MonoBehaviour mb) 
                    UnityEngine.Object.Destroy(mb.gameObject);
            });
            
            Log.Debug(LogTags.Services, $"[UIPoolManager] Cleared pool for {windowType.Name}");
        }

        public int GetPoolSize(Type windowType) => _windowPool.GetSize(windowType);

        public Dictionary<Type, int> GetPoolStats() => _windowPool.GetStats();
    }
}
