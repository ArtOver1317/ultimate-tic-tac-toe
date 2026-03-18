using System;
using Runtime.Gameplay;
using Runtime.Games.Battleship.Core;
using Runtime.Games.TicTacToe.AI.Profiles;
using Runtime.Games.TicTacToe.AI.Ultimate.Profiles;
using Runtime.Infrastructure.Logging;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;
using VContainer.Unity;

namespace Runtime.Infrastructure.Scopes
{
    public sealed class GameplayLifetimeScope : LifetimeScope
    {
        [SerializeField] private UIDocument GameplayDocument;
        [SerializeField] private BotProfileCatalog BotProfileCatalog;
        [SerializeField] private BotSearchSettings BotSearchSettings;
        [SerializeField] private UltimateBotProfileCatalog UltimateBotProfileCatalog;
        [SerializeField] private BattleshipGameplaySettings BattleshipGameplaySettingsAsset;

        protected override void Configure(IContainerBuilder builder)
        {
            ValidateSerializedFields();
            GameplayScopeCoreRegistration.Register(builder, GameplayDocument, BattleshipGameplaySettingsAsset);
            GameplayScopeEcsRegistration.Register(builder);
            GameplayScopeServicesRegistration.Register(builder);
            GameplayScopeBotRegistration.Register(builder, BotProfileCatalog, BotSearchSettings, UltimateBotProfileCatalog);
            GameplayScopeUiRegistration.Register(builder);
            GameplayScopeStartupRegistration.Register(builder);
        }

        private void ValidateSerializedFields()
        {
            if (GameplayDocument == null)
                throw new InvalidOperationException("Gameplay UIDocument is not assigned.");

            if (BattleshipGameplaySettingsAsset == null)
            {
                GameLog.Warning("[GameplayLifetimeScope] BattleshipGameplaySettings is not assigned. Runtime defaults will be used.");
                BattleshipGameplaySettingsAsset = BattleshipGameplaySettings.CreateRuntimeDefault();
            }
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
                catch (Exception ex)
                {
                    GameLog.Warning($"[GameplayLifetimeScope] Error during cleanup in {nameof(OnDestroy)}. Error={ex.Message}");
                }
            }

            base.OnDestroy();
        }
    }
}
