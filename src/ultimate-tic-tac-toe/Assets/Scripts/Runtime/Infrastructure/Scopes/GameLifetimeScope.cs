using System;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.Matchmaking.Config;
using Runtime.Infrastructure.EntryPoint;
using Runtime.Infrastructure.Logging;
using Runtime.Services.Assets;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Runtime.Infrastructure.Scopes
{
    public class GameLifetimeScope : LifetimeScope
    {
        [SerializeField] private AssetLibrary AssetLibrary;
        [SerializeField] private MoveTimerPresetsConfig MoveTimerPresets;
        [SerializeField] private MatchmakingConfigAsset MatchmakingConfig;
        [SerializeField] private UIWindowBootstrapper UIBootstrapper;

        protected override void Configure(IContainerBuilder builder)
        {
            builder.RegisterEntryPoint<GameEntryPoint>();

            EnsureSerializedFieldsAssigned();
            GameScopeCoreRegistration.Register(builder, AssetLibrary, MoveTimerPresets);
            GameScopeWizardRegistration.Register(builder);
            GameScopeOnlineRegistration.Register(builder, MatchmakingConfig);
            GameScopeLocalizationRegistration.Register(builder);
            GameScopePlayerStateRegistration.Register(builder);
            GameScopeUiRegistration.Register(builder);

            if (UIBootstrapper != null)
                builder.RegisterComponent(UIBootstrapper);
        }

        private void EnsureSerializedFieldsAssigned()
        {
            if (AssetLibrary == null)
                throw new InvalidOperationException("AssetLibrary is not assigned in GameLifetimeScope.");

            if (MoveTimerPresets == null)
            {
                GameLog.Warning("[GameLifetimeScope] MoveTimerPresetsConfig is not assigned. Runtime defaults will be used.");
                MoveTimerPresets = MoveTimerPresetsConfig.CreateRuntimeDefault();
            }

            if (MatchmakingConfig == null)
            {
                GameLog.Warning("[GameLifetimeScope] MatchmakingConfigAsset is not assigned. Runtime defaults will be used.");
                MatchmakingConfig = MatchmakingConfigAsset.CreateRuntimeDefault();
            }
        }

        protected override void Awake()
        {
            base.Awake(); 
            DontDestroyOnLoad(gameObject);
        }
    }
}