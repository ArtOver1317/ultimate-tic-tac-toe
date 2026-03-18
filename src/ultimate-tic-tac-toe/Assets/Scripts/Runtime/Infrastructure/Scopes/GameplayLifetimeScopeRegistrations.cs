using System;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.Modes;
using Runtime.GameModes.Wizard.Online;
using Runtime.Gameplay;
using Runtime.Gameplay.ECS.Lifecycle;
using Runtime.Gameplay.ECS.Pipeline;
using Runtime.Gameplay.ECS.Publishing;
using Runtime.Gameplay.Startup;
using Runtime.Games.Battleship.AI;
using Runtime.Games.Battleship.Core;
using Runtime.Games.Battleship.ECS.Core;
using Runtime.Games.Battleship.Networking;
using Runtime.Games.Battleship.Placement;
using Runtime.Games.Battleship.Recovery;
using Runtime.Games.Battleship.State;
using Runtime.Games.Battleship.Startup;
using Runtime.Games.Battleship.UI;
using Runtime.Games.Battleship.UI.Board;
using Runtime.Games.Battleship.UI.Placement;
using Runtime.Games.TicTacToe;
using Runtime.Games.TicTacToe.AI.Core;
using Runtime.Games.TicTacToe.AI.Profiles;
using Runtime.Games.TicTacToe.AI.Search;
using Runtime.Games.TicTacToe.AI.Turns;
using Runtime.Games.TicTacToe.AI.Ultimate.Core;
using Runtime.Games.TicTacToe.AI.Ultimate.Decision;
using Runtime.Games.TicTacToe.AI.Ultimate.Execution;
using Runtime.Games.TicTacToe.AI.Ultimate.Profiles;
using Runtime.Games.TicTacToe.ECS;
using Runtime.Games.TicTacToe.Rules;
using Runtime.Games.TicTacToe.Series;
using Runtime.Games.TicTacToe.Ultimate;
using Runtime.Games.TicTacToe.Ultimate.Rules;
using Runtime.Infrastructure.GameStateMachine;
using Runtime.Infrastructure.Logging;
using Runtime.Localization;
using Runtime.PlayerProfile;
using Runtime.PlayerStatistics;
using UnityEngine.UIElements;
using VContainer;

namespace Runtime.Infrastructure.Scopes
{
    internal static class GameplayScopeCoreRegistration
    {
        public static void Register(IContainerBuilder builder, UIDocument gameplayDocument, BattleshipGameplaySettings battleshipGameplaySettingsAsset)
        {
            builder.RegisterInstance(gameplayDocument);
            builder.RegisterInstance(battleshipGameplaySettingsAsset);
            builder.Register<FieldSpecMapper>(Lifetime.Scoped);
            builder.Register<IGameService, LocalGameService>(Lifetime.Scoped);
        }
    }

    internal static class GameplayScopeEcsRegistration
    {
        public static void Register(IContainerBuilder builder)
        {
            builder.Register<CommandQueue>(Lifetime.Scoped);
            builder.Register<IMatchEventScheduler, DeferredEventScheduler>(Lifetime.Scoped);
            builder.Register<EventPublishSystem>(Lifetime.Scoped);
            
            builder.Register<UltimateGameplayEventStream>(Lifetime.Scoped)
                .AsSelf()
                .As<IUltimateGameplayEventStream>()
                .As<IDisposable>();
            
            builder.Register<BattleshipGameplayEventStream>(Lifetime.Scoped)
                .AsSelf()
                .As<IBattleshipGameplayEventStream>()
                .As<IDisposable>();
            
            builder.Register<IRulesEngine, ClassicRulesEngine>(Lifetime.Scoped);
            builder.Register<IUltimateRulesEngine, UltimateRulesEngine>(Lifetime.Scoped);
            builder.Register<IBattleshipPlacementValidator, BattleshipPlacementValidator>(Lifetime.Scoped);
            builder.Register<IBattleshipAutoPlacer, BattleshipAutoPlacer>(Lifetime.Scoped);
            
            builder.Register<BattleshipPlacementService>(Lifetime.Scoped)
                .AsSelf()
                .As<IDisposable>();
           
            builder.Register<TicTacToeEcsRegistrar>(Lifetime.Scoped).As<IEcsGameplayRegistrar>();
            builder.Register<UltimateTicTacToeEcsRegistrar>(Lifetime.Scoped).As<IEcsGameplayRegistrar>();
            builder.Register<BattleshipEcsRegistrar>(Lifetime.Scoped).As<IEcsGameplayRegistrar>();
            
            builder.Register<MatchEcsLifecycleService>(Lifetime.Scoped)
                .AsSelf()
                .As<IMatchEcsLifecycle>();
           
            builder.Register<MatchStateProvider>(Lifetime.Scoped)
                .AsSelf()
                .As<IMatchStateProvider>()
                .As<IGameplayEventStream>()
                .As<ICurrentPlayerChangedPublisher>()
                .As<IUltimateGameplaySnapshotProvider>();
           
            builder.Register<BattleshipSnapshotProvider>(Lifetime.Scoped)
                .As<IGameplaySnapshotProvider>()
                .As<IBattleshipGameplaySnapshotProvider>()
                .AsSelf();
           
            builder.Register<BattleshipRecoveryStateApplier>(Lifetime.Scoped)
                .As<IBattleshipRecoveryStateApplier>();
        }
    }

    internal static class GameplayScopeServicesRegistration
    {
        public static void Register(IContainerBuilder builder)
        {
            builder.Register<IGameplayNetworkBridge, FileGameplayNetworkBridge>(Lifetime.Scoped);
            builder.Register<IBattleshipNetworkBridge, PhotonBattleshipNetworkBridge>(Lifetime.Scoped);
            builder.Register<IBattleshipLayoutSerializer, BattleshipLayoutSerializer>(Lifetime.Scoped);
            builder.Register<OnlineAwareGameplayCommandSink>(Lifetime.Scoped);
            
            builder.Register<BattleshipOnlineCommandSink>(Lifetime.Scoped)
                .AsSelf()
                .As<IDisposable>();
            
            builder.Register(CreateGameplayCommandSink, Lifetime.Scoped);
            builder.Register(CreateTimeSource, Lifetime.Scoped);
           
            builder.Register(CreateMoveTimerService, Lifetime.Scoped)
                .As<IMoveTimerService>()
                .As<IDisposable>();
           
            builder.Register(CreateBattleshipPlacementTimerService, Lifetime.Scoped)
                .As<IBattleshipPlacementTimerService>()
                .As<IDisposable>();
        }

        private static IGameplayCommandSink CreateGameplayCommandSink(IObjectResolver resolver)
            => IsBattleshipGame(resolver.Resolve<IGameLaunchConfigStore>())
                ? resolver.Resolve<BattleshipOnlineCommandSink>()
                : resolver.Resolve<OnlineAwareGameplayCommandSink>();

        private static ITimeSource CreateTimeSource(IObjectResolver resolver)
        {
            var session = resolver.Resolve<IOnlineGameplaySessionContextStore>().Snapshot;
            
            return session.IsOnlineDirectInvite
                ? new FusionTickTimeSource()
                : new UnscaledDeltaTimeSource();
        }

        private static IMoveTimerService CreateMoveTimerService(IObjectResolver resolver)
        {
            var session = resolver.Resolve<IOnlineGameplaySessionContextStore>().Snapshot;

            return session.IsOnlineDirectInvite
                ? new NetworkMoveTimerService(
                    resolver.Resolve<IGameLaunchConfigStore>(),
                    resolver.Resolve<IGameplayEventStream>(),
                    resolver.Resolve<IGameplayCommandSink>(),
                    resolver.Resolve<ITimeSource>(),
                    resolver.Resolve<IOnlineGameplaySessionContextStore>())
                : new LocalMoveTimerService(
                    resolver.Resolve<IGameLaunchConfigStore>(),
                    resolver.Resolve<IGameplayEventStream>(),
                    resolver.Resolve<IGameplayCommandSink>(),
                    resolver.Resolve<ITimeSource>());
        }

        private static IBattleshipPlacementTimerService CreateBattleshipPlacementTimerService(IObjectResolver resolver)
            => new BattleshipPlacementTimerService(
                resolver.Resolve<IGameLaunchConfigStore>(),
                resolver.Resolve<IBattleshipGameplayEventStream>(),
                resolver.Resolve<IBattleshipGameplaySnapshotProvider>(),
                resolver.Resolve<IMatchStateProvider>(),
                resolver.Resolve<IGameplayCommandSink>(),
                resolver.Resolve<ITimeSource>(),
                resolver.Resolve<IOnlineGameplaySessionContextStore>());

        internal static bool IsBattleshipGame(IGameLaunchConfigStore configStore)
            => configStore.TryPeek(out var config)
               && config != null
               && string.Equals(config.GameId, BattleshipStrategy.DefaultGameId, StringComparison.Ordinal);
    }

    internal static class GameplayScopeBotRegistration
    {
        public static void Register(
            IContainerBuilder builder,
            BotProfileCatalog botProfileCatalog,
            BotSearchSettings botSearchSettings,
            UltimateBotProfileCatalog ultimateBotProfileCatalog)
        {
            RegisterBotCatalogs(builder, botProfileCatalog, botSearchSettings, ultimateBotProfileCatalog);
            builder.Register<MinimaxDecisionEngine>(Lifetime.Scoped).As<IBotDecisionEngine>();
            builder.Register<ClassicWinLengthProvider>(Lifetime.Scoped).As<IClassicWinLengthProvider>();
            
            builder.Register<BotTurnDriver>(Lifetime.Scoped)
                .As<IBotTurnDriver>()
                .As<IDisposable>();
            
            builder.Register<BattleshipBotDriver>(Lifetime.Scoped)
                .As<IBattleshipBotDriver>()
                .As<IDisposable>();
            
            builder.Register<UltimateBotStateReader>(Lifetime.Scoped).As<IUltimateBotStateReader>();
            builder.Register<UltimateBotDecisionEngine>(Lifetime.Scoped).As<IUltimateBotDecisionEngine>();
            builder.Register<BotRngSessionFactory>(Lifetime.Scoped).As<IBotRngSessionFactory>();
            builder.Register<GameplayBotMoveCommandSink>(Lifetime.Scoped).As<IBotMoveCommandSink>();
            builder.Register<LocalMatchFailSafeGateway>(Lifetime.Scoped).As<IMatchFailSafeGateway>();
            builder.Register<BotTurnOrchestrator>(Lifetime.Scoped).As<IBotTurnOrchestrator>().As<IDisposable>();
        }

        private static void RegisterBotCatalogs(
            IContainerBuilder builder,
            BotProfileCatalog botProfileCatalog,
            BotSearchSettings botSearchSettings,
            UltimateBotProfileCatalog ultimateBotProfileCatalog)
        {
            if (botProfileCatalog != null)
                builder.RegisterInstance(botProfileCatalog).As<IBotProfileCatalog>();
            else
                builder.RegisterInstance(new EmptyBotProfileCatalog()).As<IBotProfileCatalog>();

            if (botSearchSettings != null)
                builder.RegisterInstance(botSearchSettings);
            else
                GameLog.Warning("[GameplayLifetimeScope] BotSearchSettings is not assigned. Bot search defaults (FastPveDefault) will be used.");

            if (ultimateBotProfileCatalog != null)
                builder.RegisterInstance(ultimateBotProfileCatalog).As<IUltimateBotProfileCatalog>();
            else
                builder.RegisterInstance(new EmptyUltimateBotProfileCatalog()).As<IUltimateBotProfileCatalog>();
        }
    }

    internal static class GameplayScopeUiRegistration
    {
        public static void Register(IContainerBuilder builder)
        {
            builder.Register<GameplayFieldPresenter>(Lifetime.Scoped)
                .As<IGameplayFieldPresenter>()
                .As<IGameplayFieldUiAdapter>()
                .As<IBattleshipFieldUiAdapter>();
           
            builder.Register(CreateGameplayMovesModeBehavior, Lifetime.Scoped);
            builder.Register<GameplayMovesBinder>(Lifetime.Scoped);
            builder.Register<BattleshipBoardsBinder>(Lifetime.Scoped);
           
            builder.Register<BattleshipPlacementUiController>(Lifetime.Scoped)
                .As<IBattleshipPlacementUiController>()
                .As<IDisposable>();
            
            builder.Register<MoveTimerHudViewModel>(Lifetime.Scoped)
                .As<IMoveTimerHudViewModel>()
                .As<IDisposable>();
            
            builder.Register<MoveTimerHudBinder>(Lifetime.Scoped)
                .AsSelf()
                .As<IDisposable>();
           
            builder.Register<BattleshipPlacementTimerHudViewModel>(Lifetime.Scoped)
                .As<IBattleshipPlacementTimerHudViewModel>()
                .As<IDisposable>();
            
            builder.Register<BattleshipPlacementTimerHudBinder>(Lifetime.Scoped)
                .AsSelf()
                .As<IDisposable>();
          
            builder.Register<WinLineRenderer>(Lifetime.Scoped)
                .AsSelf()
                .As<IDisposable>();
          
            builder.Register<ISeriesService, SeriesService>(Lifetime.Scoped);
           
            builder.Register<OnlinePlayerNamesStore>(Lifetime.Scoped)
                .As<IOnlinePlayerNamesStore>()
                .As<IDisposable>();
           
            builder.Register(CreateMatchPlayerNames, Lifetime.Scoped)
                .As<IMatchPlayerNames>()
                .As<IDisposable>();
           
            builder.Register<IGameplayBackHandler, GameplayBackHandler>(Lifetime.Scoped);
           
            builder.Register<PlayerStatisticsMatchReporter>(Lifetime.Scoped)
                .AsSelf()
                .As<IDisposable>();
        }

        private static IGameplayMovesModeBehavior CreateGameplayMovesModeBehavior(IObjectResolver resolver)
            => GameplayScopeServicesRegistration.IsBattleshipGame(resolver.Resolve<IGameLaunchConfigStore>())
                ? new BattleshipGameplayMovesModeBehavior(
                    resolver.Resolve<IBattleshipGameplaySnapshotProvider>(),
                    resolver.Resolve<IOnlineGameplaySessionContextStore>())
                : DefaultGameplayMovesModeBehavior.Instance;

        private static IMatchPlayerNames CreateMatchPlayerNames(IObjectResolver resolver)
        {
            var session = resolver.Resolve<IOnlineGameplaySessionContextStore>().Snapshot;

            return session.IsOnlineDirectInvite
                ? new OnlineMatchPlayerNames(
                    resolver.Resolve<IOnlineGameplaySessionContextStore>(),
                    resolver.Resolve<IPlayerNameService>(),
                    resolver.Resolve<IOnlinePlayerNamesStore>(),
                    resolver.Resolve<ILocalizationService>())
                : new LocalMatchPlayerNames(
                    resolver.Resolve<IPlayerNameService>(),
                    resolver.Resolve<ILocalizationService>());
        }
    }

    internal static class GameplayScopeStartupRegistration
    {
        public static void Register(IContainerBuilder builder)
        {
            RegisterDependencies(builder);
            RegisterCoordinators(builder);
        
            builder.Register<BattleshipGameplayStartup>(Lifetime.Scoped)
                .AsSelf()
                .As<IDisposable>();
           
            builder.Register<TicTacToeGameplayStartup>(Lifetime.Scoped)
                .AsSelf()
                .As<IDisposable>();
          
            builder.Register(CreateGameplayStartup, Lifetime.Scoped);
        }

        private static void RegisterDependencies(IContainerBuilder builder)
        {
            builder.Register<GameplayStartupRuntimeState>(Lifetime.Scoped)
                .AsSelf();
          
            builder.Register(CreateCoreServices, Lifetime.Scoped)
                .AsSelf();
          
            builder.Register(CreateTimerServices, Lifetime.Scoped)
                .AsSelf();
           
            builder.Register(CreateBotServices, Lifetime.Scoped)
                .AsSelf();
           
            builder.Register(CreateOnlineServices, Lifetime.Scoped)
                .AsSelf();
          
            builder.Register(CreateBattleshipServices, Lifetime.Scoped)
                .AsSelf();
           
            builder.Register(resolver => new GameplayStartupDependencies(
                        resolver.Resolve<GameplayStartupCoreServices>(),
                        resolver.Resolve<GameplayStartupTimerServices>(),
                        resolver.Resolve<GameplayStartupBotServices>(),
                        resolver.Resolve<GameplayStartupOnlineServices>(),
                        resolver.Resolve<GameplayStartupBattleshipServices>()),
                    Lifetime.Scoped)
                .AsSelf();
        }

        private static void RegisterCoordinators(IContainerBuilder builder)
        {
            builder.Register<GameplayStartupBattleshipSessionScoreStore>(Lifetime.Scoped)
                .AsSelf();
           
            builder.Register<GameplayStartupUiCoordinator>(Lifetime.Scoped)
                .AsSelf();
          
            builder.Register<GameplayStartupBotCoordinator>(Lifetime.Scoped)
                .AsSelf();
           
            builder.Register<GameplayStartupBattleshipRecoveryCoordinator>(Lifetime.Scoped)
                .AsSelf();
            
            builder.Register<GameplayStartupOnlineCoordinator>(Lifetime.Scoped)
                .AsSelf();
          
            builder.Register<GameplayStartupRoundCoordinator>(Lifetime.Scoped)
                .AsSelf();
        }

        private static GameplayStartupCoreServices CreateCoreServices(IObjectResolver resolver)
            => new(
                resolver.Resolve<IGameLaunchConfigStore>(),
                resolver.Resolve<IGameService>(),
                resolver.Resolve<IGameplayFieldPresenter>(),
                resolver.Resolve<IGameplayFieldUiAdapter>(),
                resolver.Resolve<IMatchEcsLifecycle>(),
                resolver.Resolve<IGameplayEventStream>(),
                resolver.Resolve<IGameplayCommandSink>(),
                resolver.Resolve<GameplayMovesBinder>(),
                resolver.Resolve<WinLineRenderer>(),
                resolver.Resolve<ISeriesService>(),
                resolver.Resolve<IGameplayBackHandler>(),
                resolver.Resolve<IGameStateMachine>(),
                resolver.Resolve<ILocalizationService>(),
                resolver.Resolve<IMatchPlayerNames>(),
                resolver.Resolve<PlayerStatisticsMatchReporter>());

        private static GameplayStartupTimerServices CreateTimerServices(IObjectResolver resolver)
            => new(
                resolver.Resolve<IMoveTimerService>(),
                resolver.Resolve<IBattleshipPlacementTimerService>(),
                resolver.Resolve<MoveTimerHudBinder>(),
                resolver.Resolve<BattleshipPlacementTimerHudBinder>());

        private static GameplayStartupBotServices CreateBotServices(IObjectResolver resolver)
            => new(
                resolver.Resolve<IBotTurnDriver>(),
                resolver.Resolve<IBattleshipBotDriver>(),
                resolver.Resolve<IBotTurnOrchestrator>(),
                resolver.Resolve<IMatchFailSafeGateway>(),
                resolver.Resolve<IUltimateGameplaySnapshotProvider>(),
                resolver.Resolve<IUltimateGameplayEventStream>());

        private static GameplayStartupOnlineServices CreateOnlineServices(IObjectResolver resolver)
            => new(
                resolver.Resolve<IGameplayNetworkBridge>(),
                resolver.Resolve<IBattleshipNetworkBridge>(),
                resolver.Resolve<IOnlineGameplaySessionContextStore>(),
                resolver.Resolve<IOnlineSessionFlowService>(),
                resolver.Resolve<IOnlineSessionLauncher>(),
                resolver.Resolve<IOnlinePlayerNamesStore>(),
                resolver.Resolve<IMatchStateProvider>());

        private static GameplayStartupBattleshipServices CreateBattleshipServices(IObjectResolver resolver)
            => new(
                resolver.Resolve<IBattleshipLayoutSerializer>(),
                resolver.Resolve<BattleshipBoardsBinder>(),
                resolver.Resolve<IBattleshipPlacementUiController>(),
                resolver.Resolve<IBattleshipGameplaySnapshotProvider>(),
                resolver.Resolve<IBattleshipGameplayEventStream>(),
                resolver.Resolve<IBattleshipRecoveryStateApplier>());

        private static IGameplayStartup CreateGameplayStartup(IObjectResolver resolver)
            => GameplayScopeServicesRegistration.IsBattleshipGame(resolver.Resolve<IGameLaunchConfigStore>())
                ? resolver.Resolve<BattleshipGameplayStartup>()
                : resolver.Resolve<TicTacToeGameplayStartup>();
    }
}