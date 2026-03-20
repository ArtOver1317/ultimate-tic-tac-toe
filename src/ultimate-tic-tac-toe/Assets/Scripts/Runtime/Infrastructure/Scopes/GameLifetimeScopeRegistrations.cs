using System;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.Coordinator;
using Runtime.GameModes.Wizard.Matchmaking.Config;
using Runtime.GameModes.Wizard.Matchmaking.Contracts;
using Runtime.GameModes.Wizard.Matchmaking.Runtime;
using Runtime.GameModes.Wizard.Matchmaking.Services;
using Runtime.GameModes.Wizard.Modes;
using Runtime.GameModes.Wizard.Online;
using Runtime.GameModes.Wizard.Online.Flow;
using Runtime.GameModes.Wizard.Session;
using Runtime.GameModes.Wizard.ViewModels;
using Runtime.GameModes.Wizard.ViewModels.MatchSetup;
using Runtime.Gameplay;
using Runtime.Infrastructure.GameStateMachine;
using Runtime.Infrastructure.GameStateMachine.States;
using Runtime.Infrastructure.Save;
using Runtime.Infrastructure.Save.Backends;
using Runtime.Infrastructure.Save.Serialization;
using Runtime.Localization;
using Runtime.Localization.Contracts;
using Runtime.Localization.Infrastructure;
using Runtime.Localization.Services;
using Runtime.PlayerProfile;
using Runtime.PlayerStatistics;
using Runtime.Services.Assets;
using Runtime.Services.Scenes;
using Runtime.Services.UI;
using Runtime.Services.UI.Assets;
using Runtime.UI.Core;
using Runtime.UI.GameModes.Wizard;
using Runtime.UI.MainMenu;
using Runtime.UI.Settings;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Runtime.Infrastructure.Scopes
{
    internal static class GameScopeCoreRegistration
    {
        public static void Register(IContainerBuilder builder, AssetLibrary assetLibrary, MoveTimerPresetsConfig moveTimerPresets)
        {
            builder.RegisterInstance(assetLibrary);
            builder.RegisterInstance(moveTimerPresets);
            builder.Register<IAssetProvider, AddressablesAssetProvider>(Lifetime.Singleton);
            builder.Register<ISceneLoaderService, SceneLoaderService>(Lifetime.Singleton);
            builder.Register<ViewModelFactory>(Lifetime.Singleton);
            builder.Register<UIPoolManager>(Lifetime.Singleton);
            builder.Register<ObjectPool<IUIView>>(Lifetime.Singleton).As<IObjectPool<IUIView>>();
            builder.Register<ObjectPool<BaseViewModel>>(Lifetime.Singleton).As<IObjectPool<BaseViewModel>>();
            builder.Register<IViewAssetProvider, AddressablesViewAssetProvider>(Lifetime.Singleton);
            builder.Register<IUIService, UIService>(Lifetime.Singleton);
            builder.Register<IMainMenuEntryModeStore, MainMenuEntryModeStore>(Lifetime.Singleton);
        }
    }

    internal static class GameScopeWizardRegistration
    {
        public static void Register(IContainerBuilder builder)
        {
            builder.Register<IGameLaunchConfigStore, GameLaunchConfigStore>(Lifetime.Singleton);
            RegisterSessionFactory(builder);
            builder.Register<IGameWizardCoordinator, GameWizardCoordinator>(Lifetime.Singleton);
            builder.Register<IGameWizardNavigator, GameWizardNavigator>(Lifetime.Singleton);
            builder.Register<IGameCatalog, GameCatalog>(Lifetime.Singleton);
            builder.Register<IBotDifficultyCatalog, BotDifficultyCatalog>(Lifetime.Singleton);
            RegisterViewModels(builder);
            RegisterGameStrategies(builder);
        }

        private static void RegisterSessionFactory(IContainerBuilder builder)
        {
            builder.Register<IGameSession>(resolver =>
                    new GameSession(resolver.Resolve<IGameCatalog>()),
                Lifetime.Transient);

            builder.Register<Func<IGameSession>>(
                resolver => () => resolver.Resolve<IGameSession>(),
                Lifetime.Singleton);
        }

        private static void RegisterViewModels(IContainerBuilder builder)
        {
            builder.Register<GameSelectionViewModel>(Lifetime.Transient);
            builder.Register<MatchSetupViewModel>(Lifetime.Transient);
            builder.Register<MatchmakingViewModel>(Lifetime.Transient);
            builder.Register<TicTacToeSettingsViewModel>(Lifetime.Transient);
            builder.Register<UltimateTicTacToeSettingsViewModel>(Lifetime.Transient);
            builder.Register<BattleshipSettingsViewModel>(Lifetime.Transient);
            builder.Register<PlayerNameEditViewModel>(Lifetime.Transient);
        }

        private static void RegisterGameStrategies(IContainerBuilder builder)
        {
            builder.Register<Func<TicTacToeSettingsViewModel>>(
                resolver => () => resolver.Resolve<TicTacToeSettingsViewModel>(),
                Lifetime.Singleton);

            builder.Register<Func<UltimateTicTacToeSettingsViewModel>>(
                resolver => () => resolver.Resolve<UltimateTicTacToeSettingsViewModel>(),
                Lifetime.Singleton);

            builder.Register<Func<BattleshipSettingsViewModel>>(
                resolver => () => resolver.Resolve<BattleshipSettingsViewModel>(),
                Lifetime.Singleton);

            builder.Register(resolver =>
                        new TicTacToeStrategy(resolver.Resolve<Func<TicTacToeSettingsViewModel>>()),
                    Lifetime.Singleton)
                .As<IGameStrategy>();

            builder.Register(resolver =>
                        new UltimateTicTacToeStrategy(resolver.Resolve<Func<UltimateTicTacToeSettingsViewModel>>()),
                    Lifetime.Singleton)
                .As<IGameStrategy>();

            builder.Register(resolver =>
                        new BattleshipStrategy(resolver.Resolve<Func<BattleshipSettingsViewModel>>()),
                    Lifetime.Singleton)
                .As<IGameStrategy>();

            builder.Register<IGameSettingsBinder, TicTacToeSettingsBinder>(Lifetime.Singleton);
            builder.Register<IGameSettingsBinder, UltimateTicTacToeSettingsBinder>(Lifetime.Singleton);
            builder.Register<IGameSettingsBinder, BattleshipSettingsBinder>(Lifetime.Singleton);
        }
    }

    internal static class GameScopeOnlineRegistration
    {
        public static void Register(IContainerBuilder builder, MatchmakingConfigAsset matchmakingConfig)
        {
            builder.Register<IMatchmakingConfig>(_ => matchmakingConfig, Lifetime.Singleton);
            builder.Register<IMatchmakingService, PhotonMatchmakingService>(Lifetime.Singleton);
            builder.Register(_ => new OnlineSessionIdLifecycle(), Lifetime.Singleton);
            builder.Register<IOnlineSessionFlowService, OnlineSessionFlowService>(Lifetime.Singleton);
            builder.Register<IOnlineCountdownSyncService, OnlineCountdownSyncService>(Lifetime.Singleton);
            builder.Register<IOnlineGameplaySessionContextStore, OnlineGameplaySessionContextStore>(Lifetime.Singleton);
            builder.Register(_ => new OnlineDiagnosticsBuffer(), Lifetime.Singleton);
            builder.Register<OnlineCleanupTracker>(Lifetime.Singleton);
            builder.Register(CreatePhotonTransport, Lifetime.Singleton);
            builder.Register<PhotonSessionGateway>(Lifetime.Singleton);
            builder.Register<IPhotonSessionGateway>(resolver => resolver.Resolve<PhotonSessionGateway>(), Lifetime.Singleton);
            builder.Register<IOnlineSessionLauncher, OnlineSessionLauncher>(Lifetime.Singleton);
            builder.Register<IGameplayScopeAccessor, GameplayScopeAccessor>(Lifetime.Singleton);
        }

        private static IPhotonSessionTransport CreatePhotonTransport(IObjectResolver _)
        {
            var go = new GameObject("OnlineFusionTransport");
            UnityEngine.Object.DontDestroyOnLoad(go);
            return go.AddComponent<FusionSessionTransport>();
        }
    }

    internal static class GameScopeLocalizationRegistration
    {
        public static void Register(IContainerBuilder builder)
        {
            builder.Register<ILocalizationPolicy>(_ =>
                    new GameLocalizationPolicy(
                        useMissingKeyPlaceholders: true,
                        maxCachedTables: 32,
                        defaultLocale: null),
                Lifetime.Singleton);

#if UNITY_WEBGL
            builder.Register<PlayerPrefsSaveBackend>(Lifetime.Singleton).As<ISaveBackend>();
#else
            builder.Register<FileSaveBackend>(Lifetime.Singleton).As<ISaveBackend>();
#endif

            builder.Register<SaveEncryptor>(Lifetime.Singleton);
            builder.Register<SaveSerializer>(Lifetime.Singleton);
            
            builder.Register<SaveService>(Lifetime.Singleton)
                .As<ISaveService>()
                .As<ISaveServiceWithResult>()
                .As<IInitializable>()
                .AsSelf();
            
            builder.Register<SaveServiceLocaleStorage>(Lifetime.Singleton).As<ILocaleStorage>();
            builder.Register<ILocalizationCatalog, AddressablesLocalizationCatalog>(Lifetime.Singleton);
            builder.Register<ILocalizationLoader, AddressablesLocalizationLoader>(Lifetime.Singleton);
            builder.Register<ILocalizationParser, JsonLocalizationParser>(Lifetime.Singleton);
            builder.Register<ILocalizationStore, LocalizationStore>(Lifetime.Singleton);
            builder.Register<ITextFormatter, NamedArgsFormatter>(Lifetime.Singleton);
            builder.Register<ILocalizationService, LocalizationService>(Lifetime.Singleton);
        }
    }

    internal static class GameScopePlayerStateRegistration
    {
        public static void Register(IContainerBuilder builder)
        {
            builder.Register<PlayerNameService>(Lifetime.Singleton).As<IPlayerNameService>().As<IInitializable>().AsSelf();
            builder.Register<PlayerStatisticsService>(Lifetime.Singleton).As<IPlayerStatisticsService>().As<IInitializable>().AsSelf();
            builder.Register<MatchOutcomeResolver>(Lifetime.Singleton).As<IMatchOutcomeResolver>().AsSelf();
            builder.Register<MatchKeyMapper>(Lifetime.Singleton).AsSelf();
            builder.Register<IStateFactory, StateFactory>(Lifetime.Singleton);
            builder.Register<IGameStateMachine, GameStateMachine.GameStateMachine>(Lifetime.Singleton);
            builder.Register<BootstrapState>(Lifetime.Transient);
            builder.Register<LoadMainMenuState>(Lifetime.Transient);
            builder.Register<MainMenuState>(Lifetime.Transient);
            builder.Register<LoadGameplayState>(Lifetime.Transient);
            builder.Register<GameplayState>(Lifetime.Transient);
        }
    }

    internal static class GameScopeUiRegistration
    {
        public static void Register(IContainerBuilder builder)
            => builder.Register<IMainMenuCoordinator, MainMenuCoordinator>(Lifetime.Transient);
    }
}