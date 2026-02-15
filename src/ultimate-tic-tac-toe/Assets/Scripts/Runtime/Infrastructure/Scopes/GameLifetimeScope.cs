using System;
using Runtime.Infrastructure.EntryPoint;
using Runtime.Infrastructure.GameStateMachine;
using Runtime.Infrastructure.GameStateMachine.States;
using Runtime.GameModes.Wizard;
using Runtime.Localization;
using Runtime.Services.Assets;
using Runtime.Services.Scenes;
using Runtime.Services.UI;
using Runtime.Services.UI.Assets;
using Runtime.UI.MainMenu;
using Runtime.UI.Core;
using Runtime.UI.GameModes.Wizard;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Runtime.Infrastructure.Scopes
{
    public class GameLifetimeScope : LifetimeScope
    {
        [SerializeField] private AssetLibrary _assetLibrary;

        protected override void Configure(IContainerBuilder builder)
        {
            builder.RegisterEntryPoint<GameEntryPoint>();

            if (_assetLibrary == null)
                throw new InvalidOperationException("AssetLibrary is not assigned in GameLifetimeScope.");
            
            // Services
            builder.RegisterInstance(_assetLibrary);
            builder.Register<IAssetProvider, AddressablesAssetProvider>(Lifetime.Singleton);
            builder.Register<ISceneLoaderService, SceneLoaderService>(Lifetime.Singleton);
            builder.Register<ViewModelFactory>(Lifetime.Singleton);
            builder.Register<UIPoolManager>(Lifetime.Singleton);
            builder.Register<ObjectPool<IUIView>>(Lifetime.Singleton).As<IObjectPool<IUIView>>();
            builder.Register<ObjectPool<BaseViewModel>>(Lifetime.Singleton).As<IObjectPool<BaseViewModel>>();
            builder.Register<IViewAssetProvider, AddressablesViewAssetProvider>(Lifetime.Singleton);
            builder.Register<IUIService, UIService>(Lifetime.Singleton);
            builder.Register<IMainMenuEntryModeStore, MainMenuEntryModeStore>(Lifetime.Singleton);

            // Game Mode Wizard
            builder.Register<IGameLaunchConfigStore, GameLaunchConfigStore>(Lifetime.Singleton);
            
            builder.Register<IGameSession>(resolver =>
                    new GameSession(resolver.Resolve<IGameCatalog>()),
                Lifetime.Transient);
            
            builder.Register<Func<IGameSession>>(
                resolver => () => resolver.Resolve<IGameSession>(),
                Lifetime.Singleton);
            
            builder.Register<IGameWizardCoordinator, GameWizardCoordinator>(Lifetime.Singleton);
            builder.Register<IGameWizardNavigator, GameWizardNavigator>(Lifetime.Singleton);
            builder.Register<IGameCatalog, GameCatalog>(Lifetime.Singleton);
            builder.Register<IBotDifficultyCatalog, BotDifficultyCatalog>(Lifetime.Singleton);
            builder.Register<IMatchmakingService, MatchmakingServiceStub>(Lifetime.Singleton);

            builder.Register<Gameplay.IGameplayScopeAccessor, Gameplay.GameplayScopeAccessor>(Lifetime.Singleton);

            builder.Register<GameSelectionViewModel>(Lifetime.Transient);
            builder.Register<MatchSetupViewModel>(Lifetime.Transient);
            builder.Register<MatchmakingViewModel>(Lifetime.Transient);
            builder.Register<TicTacToeSettingsViewModel>(Lifetime.Transient);
            builder.Register<UltimateTicTacToeSettingsViewModel>(Lifetime.Transient);

            builder.Register<Func<TicTacToeSettingsViewModel>>(
                resolver => () => resolver.Resolve<TicTacToeSettingsViewModel>(),
                Lifetime.Singleton);

            builder.Register<Func<UltimateTicTacToeSettingsViewModel>>(
                resolver => () => resolver.Resolve<UltimateTicTacToeSettingsViewModel>(),
                Lifetime.Singleton);

            builder.Register(resolver =>
                        new TicTacToeStrategy(resolver.Resolve<Func<TicTacToeSettingsViewModel>>()),
                    Lifetime.Singleton)
                .As<IGameStrategy>();

            builder.Register(resolver =>
                        new UltimateTicTacToeStrategy(resolver.Resolve<Func<UltimateTicTacToeSettingsViewModel>>()),
                    Lifetime.Singleton)
                .As<IGameStrategy>();

            builder.Register<IGameSettingsBinder, TicTacToeSettingsBinder>(Lifetime.Singleton);
            builder.Register<IGameSettingsBinder, UltimateTicTacToeSettingsBinder>(Lifetime.Singleton);
            
            // Localization Services
            // Note: Factory registration required - VContainer cannot auto-resolve constructors with optional parameters.
            // Even though values match constructor defaults, they must be specified explicitly for DI container.
            builder.Register<ILocalizationPolicy>(_ => 
                    new GameLocalizationPolicy(
                        useMissingKeyPlaceholders: true, 
                        maxCachedTables: 32, 
                        defaultLocale: null), 
                Lifetime.Singleton);
            
            builder.Register<ILocaleStorage, PlayerPrefsLocaleStorage>(Lifetime.Singleton);
            builder.Register<ILocalizationCatalog, AddressablesLocalizationCatalog>(Lifetime.Singleton);
            builder.Register<ILocalizationLoader, AddressablesLocalizationLoader>(Lifetime.Singleton);
            builder.Register<ILocalizationParser, JsonLocalizationParser>(Lifetime.Singleton);
            builder.Register<ILocalizationStore, LocalizationStore>(Lifetime.Singleton);
            builder.Register<ITextFormatter, NamedArgsFormatter>(Lifetime.Singleton);
            builder.Register<ILocalizationService, LocalizationService>(Lifetime.Singleton);
        
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
