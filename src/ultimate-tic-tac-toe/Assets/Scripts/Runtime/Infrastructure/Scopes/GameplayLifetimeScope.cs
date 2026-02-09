using Runtime.Gameplay;
using Runtime.Games.TicTacToe;
using Runtime.Games.TicTacToe.Moves;
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
            builder.Register<ILocalMovesService, LocalMovesService>(Lifetime.Scoped);

            // Phase 4: default VFX settings for local moves.
            builder.RegisterInstance(MovesVfxSettings.Default);

            builder.Register<GameplayFieldPresenter>(Lifetime.Scoped)
                .As<IGameplayFieldPresenter>()
                .As<IGameplayFieldUiAdapter>();
            builder.Register<GameplayMovesBinder>(Lifetime.Scoped);
            builder.Register<IGameplayBackHandler, GameplayBackHandler>(Lifetime.Scoped);
            builder.Register<IGameplayStartup, GameplayStartup>(Lifetime.Scoped);
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
