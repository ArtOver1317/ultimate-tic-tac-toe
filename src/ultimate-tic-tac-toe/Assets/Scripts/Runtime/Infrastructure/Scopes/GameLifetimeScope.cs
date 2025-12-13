using Runtime.Infrastructure.EntryPoint;
using Runtime.Infrastructure.GameStateMachine;
using Runtime.Infrastructure.GameStateMachine.States;
using Runtime.Services.Scenes;
using Runtime.Services.UI;
using Runtime.UI.MainMenu;
using Runtime.UI.Core;
using VContainer;
using VContainer.Unity;

namespace Runtime.Infrastructure.Scopes
{
    public class GameLifetimeScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            builder.RegisterEntryPoint<GameEntryPoint>();
            
            // Services
            builder.Register<ISceneLoaderService, SceneLoaderService>(Lifetime.Singleton);
            builder.Register<ViewModelFactory>(Lifetime.Singleton);
            builder.Register<UIPoolManager>(Lifetime.Singleton);
            builder.Register<ObjectPool<IUIView>>(Lifetime.Singleton).As<IObjectPool<IUIView>>();
            builder.Register<ObjectPool<BaseViewModel>>(Lifetime.Singleton).As<IObjectPool<BaseViewModel>>();
            builder.Register<IUIService, UIService>(Lifetime.Singleton);
        
            // State Machine
            builder.Register<IStateFactory, StateFactory>(Lifetime.Singleton);
            builder.Register<IGameStateMachine, GameStateMachine.GameStateMachine>(Lifetime.Singleton);
        
            // States
            builder.Register<BootstrapState>(Lifetime.Transient);
            builder.Register<LoadMainMenuState>(Lifetime.Transient);
            builder.Register<MainMenuState>(Lifetime.Transient);
            builder.Register<LoadGameplayState>(Lifetime.Transient);
            builder.Register<GameplayState>(Lifetime.Transient);

            // UI
            builder.Register<IMainMenuCoordinator, MainMenuCoordinator>(Lifetime.Transient);
        }
        
        protected override void Awake()
        {
            base.Awake(); 
            DontDestroyOnLoad(gameObject);
        }
    }
}
