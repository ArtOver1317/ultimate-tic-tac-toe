using System;
using System.Collections.Generic;
using System.Linq;
using Runtime.Infrastructure.Logging;
using Runtime.UI.Core;
using StripLog;
using VContainer;

namespace Runtime.Services.UI
{
    public class ViewModelFactory
    {
        private readonly IObjectResolver _container;
        private readonly Dictionary<Type, Func<object[], object>> _cachedFactories = new();
        private readonly Dictionary<Type, Type[]> _cachedDependencies = new();

        public ViewModelFactory(IObjectResolver container) => _container = container;
        
        public TViewModel CreateViewModel<TViewModel>() where TViewModel : BaseViewModel
        {
            var viewModelType = typeof(TViewModel);
            var registeredViewModel = TryResolveFromContainer<TViewModel>(viewModelType);
            return registeredViewModel ?? CreateViewModelWithDependencies<TViewModel>(viewModelType);
        }

        private TViewModel TryResolveFromContainer<TViewModel>(Type viewModelType) where TViewModel : BaseViewModel
        {
            try
            {
                var registered = (TViewModel)_container.Resolve(viewModelType);
                Log.Debug(LogTags.Services, $"[ViewModelFactory] ViewModel {viewModelType.Name} resolved from DI container");
                return registered;
            }
            catch
            {
                // Container doesn't have this type registered, will create manually
                return null;
            }
        }

        private TViewModel CreateViewModelWithDependencies<TViewModel>(Type viewModelType) 
            where TViewModel : BaseViewModel
        {
            EnsureFactoryIsCached(viewModelType);

            var dependencies = ResolveDependencies(viewModelType);
            var factory = _cachedFactories[viewModelType];
            var viewModel = (TViewModel)factory(dependencies);

            Log.Debug(LogTags.Services, $"[ViewModelFactory] Created {viewModelType.Name} with {dependencies.Length} dependencies");
            return viewModel;
        }

        private void EnsureFactoryIsCached(Type viewModelType)
        {
            if (!_cachedFactories.ContainsKey(viewModelType))
                CacheFactoryForType(viewModelType);
        }

        private object[] ResolveDependencies(Type viewModelType)
        {
            var dependencyTypes = _cachedDependencies[viewModelType];
            var resolvedDependencies = new object[dependencyTypes.Length];

            for (var i = 0; i < dependencyTypes.Length; i++)
            {
                resolvedDependencies[i] = ResolveSingleDependency(dependencyTypes[i], viewModelType);
            }

            return resolvedDependencies;
        }

        private object ResolveSingleDependency(Type dependencyType, Type viewModelType)
        {
            try
            {
                return _container.Resolve(dependencyType);
            }
            catch (Exception ex)
            {
                Log.Error(LogTags.Services, $"[ViewModelFactory] Failed to resolve {dependencyType.Name} for {viewModelType.Name}: {ex.Message}");
                return null;
            }
        }

        private void CacheFactoryForType(Type viewModelType)
        {
            var constructor = FindBestConstructor(viewModelType);

            if (constructor == null)
            {
                CacheEmptyFactory(viewModelType);
                return;
            }

            var parameterTypes = ExtractParameterTypes(constructor);
            CacheFactoryWithParameters(viewModelType, parameterTypes);
        }

        private System.Reflection.ConstructorInfo FindBestConstructor(Type viewModelType) =>
            viewModelType.GetConstructors()
                .OrderByDescending(c => c.GetParameters().Length)
                .FirstOrDefault();

        private Type[] ExtractParameterTypes(System.Reflection.ConstructorInfo constructor) =>
            constructor.GetParameters()
                .Select(p => p.ParameterType)
                .ToArray();

        private void CacheFactoryWithParameters(Type viewModelType, Type[] parameterTypes)
        {
            _cachedDependencies[viewModelType] = parameterTypes;
            _cachedFactories[viewModelType] = args => Activator.CreateInstance(viewModelType, args);
            Log.Debug(LogTags.Services, $"[ViewModelFactory] Cached factory for {viewModelType.Name} with {parameterTypes.Length} dependencies");
        }

        private void CacheEmptyFactory(Type viewModelType)
        {
            Log.Error(LogTags.Services, $"[ViewModelFactory] No public constructor found for {viewModelType.Name}");
            _cachedFactories[viewModelType] = _ => null;
            _cachedDependencies[viewModelType] = Array.Empty<Type>();
        }
    }
}

