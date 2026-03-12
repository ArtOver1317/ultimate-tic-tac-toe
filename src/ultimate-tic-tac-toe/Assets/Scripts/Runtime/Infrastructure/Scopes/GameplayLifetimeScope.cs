using System;
using Runtime.GameModes.Wizard;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.Modes;
using Runtime.GameModes.Wizard.Online;
using Runtime.Gameplay;
using Runtime.Gameplay.ECS;
using Runtime.Gameplay.ECS.Lifecycle;
using Runtime.Gameplay.ECS.Pipeline;
using Runtime.Gameplay.ECS.Publishing;
using Runtime.Games.Battleship;
using Runtime.Games.Battleship.ECS;
using Runtime.Games.TicTacToe;
using Runtime.Games.TicTacToe.AI;
using Runtime.Games.TicTacToe.AI.Ultimate;
using Runtime.Games.TicTacToe.ECS;
using Runtime.Games.TicTacToe.Moves;
using Runtime.Games.TicTacToe.Rules;
using Runtime.Games.TicTacToe.Ultimate;
using Runtime.Games.TicTacToe.Ultimate.Rules;
using Runtime.Games.TicTacToe.Series;
using Runtime.Infrastructure.Logging;
using Runtime.Localization;
using Runtime.PlayerStatistics;
using Runtime.PlayerProfile;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;
using VContainer.Unity;

namespace Runtime.Infrastructure.Scopes
{
    public sealed class GameplayLifetimeScope : LifetimeScope
    {
        [SerializeField] private UIDocument _gameplayDocument;
        [SerializeField] private BotProfileCatalog BotProfileCatalog;
        [SerializeField] private BotSearchSettings BotSearchSettings;
        [SerializeField] private UltimateBotProfileCatalog UltimateBotProfileCatalog;
        [SerializeField] private BattleshipGameplaySettings BattleshipGameplaySettingsAsset;

        protected override void Configure(IContainerBuilder builder)
        {
            if (_gameplayDocument == null)
                throw new System.InvalidOperationException("Gameplay UIDocument is not assigned.");

            if (BattleshipGameplaySettingsAsset == null)
            {
                GameLog.Warning("[GameplayLifetimeScope] BattleshipGameplaySettings is not assigned. Runtime defaults will be used.");
                BattleshipGameplaySettingsAsset = BattleshipGameplaySettings.CreateRuntimeDefault();
            }

            builder.RegisterInstance(_gameplayDocument);
            builder.RegisterInstance(BattleshipGameplaySettingsAsset);
            builder.Register<FieldSpecMapper>(Lifetime.Scoped);
            builder.Register<IGameService, LocalGameService>(Lifetime.Scoped);

            // Phase 4: default VFX settings for local moves.
            builder.RegisterInstance(MovesVfxSettings.Default);

            // ── ECS Infrastructure (Phase 5) ──
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
            builder.Register<IBattleshipPlacementService, BattleshipPlacementService>(Lifetime.Scoped)
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
            builder.Register<IGameplayNetworkBridge, FileGameplayNetworkBridge>(Lifetime.Scoped);
            builder.Register<IBattleshipNetworkBridge, FileBattleshipNetworkBridge>(Lifetime.Scoped);
            builder.Register<IBattleshipLayoutSerializer, BattleshipLayoutSerializer>(Lifetime.Scoped);
            builder.Register<OnlineAwareGameplayCommandSink>(Lifetime.Scoped);
            builder.Register<BattleshipOnlineCommandSink>(Lifetime.Scoped)
                .AsSelf()
                .As<IDisposable>();
            builder.Register<IGameplayCommandSink>(resolver =>
            {
                var configStore = resolver.Resolve<IGameLaunchConfigStore>();
                if (configStore.TryPeek(out var config)
                    && config != null
                    && string.Equals(config.GameId, BattleshipStrategy.DefaultGameId, StringComparison.Ordinal))
                {
                    return resolver.Resolve<BattleshipOnlineCommandSink>();
                }

                return resolver.Resolve<OnlineAwareGameplayCommandSink>();
            }, Lifetime.Scoped);
            builder.Register<ITimeSource>(resolver =>
            {
                var session = resolver.Resolve<IOnlineGameplaySessionContextStore>().Snapshot;
                return session.IsOnlineDirectInvite
                    ? new FusionTickTimeSource()
                    : new UnscaledDeltaTimeSource();
            }, Lifetime.Scoped);

            builder.Register<IMoveTimerService>(resolver =>
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
            }, Lifetime.Scoped)
            .As<IMoveTimerService>()
            .As<IDisposable>();

            builder.Register<IBattleshipPlacementTimerService>(resolver =>
                new BattleshipPlacementTimerService(
                    resolver.Resolve<IGameLaunchConfigStore>(),
                    resolver.Resolve<IBattleshipGameplayEventStream>(),
                    resolver.Resolve<IBattleshipGameplaySnapshotProvider>(),
                    resolver.Resolve<IMatchStateProvider>(),
                    resolver.Resolve<IGameplayCommandSink>(),
                    resolver.Resolve<ITimeSource>(),
                    resolver.Resolve<IOnlineGameplaySessionContextStore>()),
                Lifetime.Scoped)
            .As<IBattleshipPlacementTimerService>()
            .As<IDisposable>();

            // ── Bot AI ──
            if (BotProfileCatalog != null)
                builder.RegisterInstance(BotProfileCatalog).As<IBotProfileCatalog>();
            else
                builder.RegisterInstance(new EmptyBotProfileCatalog()).As<IBotProfileCatalog>();

            if (BotSearchSettings != null)
                builder.RegisterInstance(BotSearchSettings);

            builder.Register<MinimaxDecisionEngine>(Lifetime.Scoped).As<IBotDecisionEngine>();
            builder.Register<ClassicWinLengthProvider>(Lifetime.Scoped).As<IClassicWinLengthProvider>();
            builder.Register<BotTurnDriver>(Lifetime.Scoped)
                .As<IBotTurnDriver>()
                .As<IDisposable>();
            builder.Register<BattleshipBotDriver>(Lifetime.Scoped)
                .As<IBattleshipBotDriver>()
                .As<IDisposable>();

            if (UltimateBotProfileCatalog != null)
                builder.RegisterInstance(UltimateBotProfileCatalog).As<IUltimateBotProfileCatalog>();
            else
                builder.RegisterInstance(new EmptyUltimateBotProfileCatalog()).As<IUltimateBotProfileCatalog>();

            builder.Register<UltimateBotStateReader>(Lifetime.Scoped).As<IUltimateBotStateReader>();
            builder.Register<UltimateBotDecisionEngine>(Lifetime.Scoped).As<IUltimateBotDecisionEngine>();
            builder.Register<BotRngSessionFactory>(Lifetime.Scoped).As<IBotRngSessionFactory>();
            builder.Register<BotRandomizer>(Lifetime.Scoped).As<IBotRandomizer>();
            builder.Register<GameplayBotMoveCommandSink>(Lifetime.Scoped).As<IBotMoveCommandSink>();
            builder.Register<LocalMatchFailSafeGateway>(Lifetime.Scoped).As<IMatchFailSafeGateway>();
            builder.Register<BotTurnOrchestrator>(Lifetime.Scoped).As<IBotTurnOrchestrator>().As<IDisposable>();
            builder.Register<UltimateBotSelfPlayRunner>(Lifetime.Scoped).As<IBotSelfPlayRunner>();

            // ── UI / Binders ──
            builder.Register<GameplayFieldPresenter>(Lifetime.Scoped)
                .As<IGameplayFieldPresenter>()
                .As<IGameplayFieldUiAdapter>()
                .As<IBattleshipFieldUiAdapter>();
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
            builder.Register<IMatchPlayerNames>(resolver =>
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
            }, Lifetime.Scoped)
            .As<IMatchPlayerNames>()
            .As<IDisposable>();
            builder.Register<IGameplayBackHandler, GameplayBackHandler>(Lifetime.Scoped);
            builder.Register<PlayerStatisticsMatchReporter>(Lifetime.Scoped)
                .AsSelf()
                .As<IDisposable>();
            builder.Register<GameplayStartup>(Lifetime.Scoped)
                .As<IGameplayStartup>()
                .As<IDisposable>();
        }

        protected override void Awake()
        {
            base.Awake();

            var accessor = Container.Resolve<IGameplayScopeAccessor>();
            accessor.SetCurrent(Container);
        }

        protected override void OnDestroy()
        {
            if (Container != null)
            {
                try
                {
                    var accessor = Container.Resolve<IGameplayScopeAccessor>();
                    accessor.Clear(Container);
                }
                catch
                {
                    // Container might be already disposed.
                }
            }

            base.OnDestroy();
        }
    }
}
