using System;
using Runtime.Gameplay;
using Runtime.Gameplay.ECS;
using Runtime.Games.TicTacToe;
using Runtime.Games.TicTacToe.ECS;
using Runtime.Games.TicTacToe.Moves;
using Runtime.Games.TicTacToe.Rules;
using Runtime.Games.TicTacToe.Series;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;
using VContainer.Unity;

namespace Runtime.Infrastructure.Scopes
{
    public sealed class GameplayLifetimeScope : LifetimeScope
    {
        [SerializeField] private UIDocument _gameplayDocument;

        protected override void Configure(IContainerBuilder builder)
        {
            if (_gameplayDocument == null)
                throw new System.InvalidOperationException("Gameplay UIDocument is not assigned.");

            builder.RegisterInstance(_gameplayDocument);
            builder.Register<FieldSpecMapper>(Lifetime.Scoped);
            builder.Register<IGameService, LocalGameService>(Lifetime.Scoped);

            // Phase 4: default VFX settings for local moves.
            builder.RegisterInstance(MovesVfxSettings.Default);

            // ── ECS Infrastructure (Phase 5) ──
            builder.Register<CommandQueue>(Lifetime.Scoped);
            builder.Register<IMatchEventScheduler, DeferredEventScheduler>(Lifetime.Scoped);
            builder.Register<EventPublishSystem>(Lifetime.Scoped);
            builder.Register<IRulesEngine, ClassicRulesEngine>(Lifetime.Scoped);
            builder.Register<TicTacToeEcsRegistrar>(Lifetime.Scoped).As<IEcsGameplayRegistrar>();
            builder.Register<MatchEcsLifecycleService>(Lifetime.Scoped)
                .AsSelf()
                .As<IMatchEcsLifecycle>();
            builder.Register<MatchStateProvider>(Lifetime.Scoped)
                .As<IMatchStateProvider>()
                .As<IGameplayCommandSink>()
                .As<IGameplayEventStream>()
                .As<IGameplaySnapshotProvider>();

            // ── UI / Binders ──
            builder.Register<GameplayFieldPresenter>(Lifetime.Scoped)
                .As<IGameplayFieldPresenter>()
                .As<IGameplayFieldUiAdapter>();
            builder.Register<GameplayMovesBinder>(Lifetime.Scoped);
            builder.Register<WinLineRenderer>(Lifetime.Scoped)
                .AsSelf()
                .As<IDisposable>();
            builder.Register<ISeriesService, SeriesService>(Lifetime.Scoped);
            builder.Register<IGameplayBackHandler, GameplayBackHandler>(Lifetime.Scoped);
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
