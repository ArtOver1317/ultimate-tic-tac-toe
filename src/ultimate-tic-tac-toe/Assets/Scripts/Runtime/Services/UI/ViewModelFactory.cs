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
        private readonly Dictionary<Type, FactoryPlan> _cachedFactoryPlans = new();

        public ViewModelFactory(IObjectResolver container) => _container = container;
        
        public TViewModel CreateViewModel<TViewModel>() where TViewModel : BaseViewModel
        {
            var viewModelType = typeof(TViewModel);
            var registeredViewModel = TryResolveRegisteredViewModel<TViewModel>(viewModelType);
            return registeredViewModel ?? CreateViewModelFromFallbackFactory<TViewModel>(viewModelType);
        }

        private TViewModel TryResolveRegisteredViewModel<TViewModel>(Type viewModelType) where TViewModel : BaseViewModel
        {
            try
            {
                var registered = (TViewModel)_container.Resolve(viewModelType);
                Log.Debug(LogTags.Services, $"[ViewModelFactory] ViewModel {viewModelType.Name} resolved from DI container");
                return registered;
            }
            catch (VContainerException)
            {
                // Container doesn't have this type registered, will create manually
                return null;
            }
        }

        private TViewModel CreateViewModelFromFallbackFactory<TViewModel>(Type viewModelType) 
            where TViewModel : BaseViewModel
        {
            var plan = GetOrCacheFactoryPlan(viewModelType);
            var dependencies = ResolveDependencies(plan, viewModelType);
            var viewModel = (TViewModel)plan.Factory(dependencies);

            Log.Debug(LogTags.Services, $"[ViewModelFactory] Created {viewModelType.Name} with {dependencies.Length} dependencies");
            return viewModel;
        }

        private FactoryPlan GetOrCacheFactoryPlan(Type viewModelType)
        {
            if (_cachedFactoryPlans.TryGetValue(viewModelType, out var cachedPlan))
                return cachedPlan;

            var plan = CreateFactoryPlan(viewModelType);
            _cachedFactoryPlans[viewModelType] = plan;
            return plan;
        }

        private object[] ResolveDependencies(FactoryPlan plan, Type viewModelType)
        {
            var resolvedDependencies = new object[plan.DependencyTypes.Length];

            for (var i = 0; i < plan.DependencyTypes.Length; i++)
            {
                resolvedDependencies[i] = ResolveDependencyOrNull(plan.DependencyTypes[i], viewModelType);
            }

            return resolvedDependencies;
        }

        private object ResolveDependencyOrNull(Type dependencyType, Type viewModelType)
        {
            try
            {
                return _container.Resolve(dependencyType);
            }
            catch (VContainerException ex)
            {
                Log.Error(LogTags.Services, $"[ViewModelFactory] Failed to resolve {dependencyType.Name} for {viewModelType.Name}: {ex.Message}");
                return null;
            }
        }

        private FactoryPlan CreateFactoryPlan(Type viewModelType)
        {
            var constructor = FindBestConstructor(viewModelType);

            if (constructor == null)
                return CreateEmptyFactoryPlan(viewModelType);

            var parameterTypes = ExtractParameterTypes(constructor);
            Log.Debug(LogTags.Services, $"[ViewModelFactory] Cached factory for {viewModelType.Name} with {parameterTypes.Length} dependencies");
            return new FactoryPlan(parameterTypes, args => Activator.CreateInstance(viewModelType, args));
        }

        private System.Reflection.ConstructorInfo FindBestConstructor(Type viewModelType) =>
            viewModelType.GetConstructors()
                .OrderByDescending(c => c.GetParameters().Length)
                .FirstOrDefault();

        private Type[] ExtractParameterTypes(System.Reflection.ConstructorInfo constructor) =>
            constructor.GetParameters()
                .Select(p => p.ParameterType)
                .ToArray();

        private FactoryPlan CreateEmptyFactoryPlan(Type viewModelType)
        {
            Log.Error(LogTags.Services, $"[ViewModelFactory] No public constructor found for {viewModelType.Name}");
            return new FactoryPlan(Array.Empty<Type>(), _ => null);
        }

        private sealed class FactoryPlan
        {
            public FactoryPlan(Type[] dependencyTypes, Func<object[], object> factory)
            {
                DependencyTypes = dependencyTypes;
                Factory = factory;
            }

            public Type[] DependencyTypes { get; }

            public Func<object[], object> Factory { get; }
        }
    }
}