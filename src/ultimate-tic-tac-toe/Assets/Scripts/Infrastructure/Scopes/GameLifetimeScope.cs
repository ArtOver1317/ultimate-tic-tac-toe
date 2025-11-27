using Infrastructure.EntryPoint;
using Infrastructure.States;
using Services.Scenes;
using Services.UI;
using UI.MainMenu;
using VContainer;
using VContainer.Unity;

namespace Infrastructure.Scopes
{
    public class GameLifetimeScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            builder.RegisterEntryPoint<GameEntryPoint>();
            
            // Services
            builder.Register<ISceneLoaderService, SceneLoaderService>(Lifetime.Singleton);
            builder.Register<UIService>(Lifetime.Singleton);
        
            // State Machine
            builder.Register<IStateFactory, StateFactory>(Lifetime.Singleton);
            builder.Register<IGameStateMachine, GameStateMachine>(Lifetime.Singleton);
        
            // States
            builder.Register<BootstrapState>(Lifetime.Transient);
            builder.Register<LoadMainMenuState>(Lifetime.Transient);
            builder.Register<MainMenuState>(Lifetime.Transient);
            builder.Register<LoadGameplayState>(Lifetime.Transient);
            builder.Register<GameplayState>(Lifetime.Transient);

            // UI
            builder.Register<MainMenuCoordinator>(Lifetime.Transient);
        }
        
        protected override void Awake()
        {
            base.Awake(); 
            DontDestroyOnLoad(gameObject);
        }
    }
}
