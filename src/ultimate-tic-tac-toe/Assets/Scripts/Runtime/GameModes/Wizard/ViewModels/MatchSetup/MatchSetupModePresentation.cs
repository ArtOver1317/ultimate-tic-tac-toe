#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.Modes;
using Runtime.Infrastructure.Logging;
using Runtime.Localization;
using Runtime.UI.Core;

namespace Runtime.GameModes.Wizard.ViewModels.MatchSetup
{
    internal sealed class MatchSetupModePresentation : IDisposable
    {
        private readonly IGameCatalog _catalog;
        private readonly ILocalizationService _localization;
        private readonly IBotDifficultyCatalog _difficultyCatalog;
        private readonly Action<IGameConfig?> _applyModeConfigToSession;
        private readonly Action _updateCanStart;
        private readonly Func<bool> _isDisposed;
        private readonly Func<bool> _isPlayerLoopDisabledForTests;

        private readonly ReactiveProperty<string> _modeTitleText = new(string.Empty);
        private readonly ReactiveProperty<string> _modeIconKey = new(string.Empty);
        private readonly ReactiveProperty<GameSettingsPresentation?> _activeSettings = new(null);
        private readonly ReactiveProperty<IReadOnlyList<BotDifficulty>> _availableDifficulties;
        private readonly ReactiveProperty<bool> _isLocalHumanSupported = new(true);

        private string? _activeModeId;
        private IDisposable? _modeTitleSubscription;
        private IDisposable? _activeConfigSubscription;
        private IGameSettingsViewModel? _activeSettingsViewModel;
        private int _isSyncingModeConfigFromSession;

        public MatchSetupModePresentation(
            IGameCatalog catalog,
            ILocalizationService localization,
            IBotDifficultyCatalog difficultyCatalog,
            Action<IGameConfig?> applyModeConfigToSession,
            Action updateCanStart,
            Func<bool> isDisposed,
            Func<bool> isPlayerLoopDisabledForTests)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _localization = localization ?? throw new ArgumentNullException(nameof(localization));
            _difficultyCatalog = difficultyCatalog ?? throw new ArgumentNullException(nameof(difficultyCatalog));
            _applyModeConfigToSession = applyModeConfigToSession ?? throw new ArgumentNullException(nameof(applyModeConfigToSession));
            _updateCanStart = updateCanStart ?? throw new ArgumentNullException(nameof(updateCanStart));
            _isDisposed = isDisposed ?? throw new ArgumentNullException(nameof(isDisposed));
            _isPlayerLoopDisabledForTests = isPlayerLoopDisabledForTests ?? throw new ArgumentNullException(nameof(isPlayerLoopDisabledForTests));
            
            _availableDifficulties = new ReactiveProperty<IReadOnlyList<BotDifficulty>>(
                _difficultyCatalog.Difficulties ?? throw new ArgumentException("Difficulty catalog returned null list.", nameof(difficultyCatalog)));
        }

        public ReadOnlyReactiveProperty<string> ModeTitleText => _modeTitleText;
        public ReadOnlyReactiveProperty<string> ModeIconKey => _modeIconKey;
        public ReadOnlyReactiveProperty<GameSettingsPresentation?> ActiveSettings => _activeSettings;
        public ReadOnlyReactiveProperty<IReadOnlyList<BotDifficulty>> AvailableDifficulties => _availableDifficulties;
        public ReadOnlyReactiveProperty<bool> IsLocalHumanSupported => _isLocalHumanSupported;

        public string? GetActiveModeId() => _activeModeId;

        public bool ApplySelectedMode(string? selectedGameId, IGameConfig? currentModeConfig)
        {
            var normalized = string.IsNullOrWhiteSpace(selectedGameId) ? null : selectedGameId;

            if (string.Equals(_activeModeId, normalized, StringComparison.Ordinal))
                return false;

            _activeModeId = normalized;
            UpdateModePresentation(normalized, currentModeConfig);
            return true;
        }

        public void ApplyModeConfigFromSession(IGameConfig? config)
        {
            if (config == null || _activeSettingsViewModel == null)
                return;

            Interlocked.Exchange(ref _isSyncingModeConfigFromSession, 1);

            try
            {
                if (!_activeSettingsViewModel.TryApplyConfig(config))
                    GameLog.Warning($"[MatchSetupViewModel] Mode config type mismatch for {_activeSettingsViewModel.GetType().Name}.");
            }
            finally
            {
                Interlocked.Exchange(ref _isSyncingModeConfigFromSession, 0);
            }
        }

        public void Reset()
        {
            _activeModeId = null;
            DisposeModeTitleSubscription();
            ReleaseActiveSettings();
            _availableDifficulties.Value = _difficultyCatalog.Difficulties;
            _isLocalHumanSupported.Value = true;
            _modeTitleText.Value = string.Empty;
            _modeIconKey.Value = string.Empty;
        }

        public void Dispose()
        {
            Reset();
            _modeTitleText.Dispose();
            _modeIconKey.Dispose();
            _activeSettings.Dispose();
            _availableDifficulties.Dispose();
            _isLocalHumanSupported.Dispose();
        }

        private void UpdateModePresentation(string? gameId, IGameConfig? currentModeConfig)
        {
            ReleaseActiveSettings();

            if (!TryResolveModeStrategy(gameId, out var strategy))
            {
                ApplyEmptyPresentation();
                return;
            }

            ApplyModeMetadata(strategy);
            ActivateModePresentation(strategy.CreatePresentation(), currentModeConfig);
        }

        private bool TryResolveModeStrategy(string? gameId, out IGameStrategy strategy)
        {
            strategy = null!;

            if (string.IsNullOrWhiteSpace(gameId))
                return false;

            if (!_catalog.TryGetStrategy(gameId, out var resolvedStrategy) || resolvedStrategy == null)
                return false;

            strategy = resolvedStrategy;
            return true;
        }

        private void ApplyEmptyPresentation()
        {
            _isLocalHumanSupported.Value = true;
            _availableDifficulties.Value = _difficultyCatalog.Difficulties;
            DisposeModeTitleSubscription();
            _modeTitleText.Value = string.Empty;
            _modeIconKey.Value = string.Empty;
            _updateCanStart();
        }

        private void ApplyModeMetadata(IGameStrategy strategy)
        {
            _isLocalHumanSupported.Value = strategy.Metadata.SupportsLocal;
            
            _availableDifficulties.Value = MatchSetupBattleshipModeRules.SelectAvailableDifficulties(
                strategy.GameId,
                _difficultyCatalog.Difficulties);
            
            _modeIconKey.Value = strategy.Metadata.IconAssetKey;
            SubscribeToModeTitle(strategy.Metadata.DisplayNameKey);
        }

        private void SubscribeToModeTitle(string displayNameKey)
        {
            DisposeModeTitleSubscription();
            
            _modeTitleSubscription = _localization
                .Observe(GetTableIdFromQualifiedKey(displayNameKey), new TextKey(displayNameKey))
                .Subscribe(SetModeTitleTextSafe);
        }

        private void ActivateModePresentation(GameSettingsPresentation presentation, IGameConfig? currentModeConfig)
        {
            _activeSettings.Value = presentation;
            _activeSettingsViewModel = presentation.ViewModel;

            if (_activeSettingsViewModel is BaseViewModel baseViewModel)
                baseViewModel.Initialize();

            ApplyModeConfigFromSession(currentModeConfig);
            _activeConfigSubscription = presentation.ViewModel.Config.Subscribe(OnActiveModeConfigChanged);
            OnActiveModeConfigChanged(presentation.ViewModel.Config.CurrentValue);
        }

        private void OnActiveModeConfigChanged(IGameConfig? config)
        {
            if (config == null)
                return;

            if (Volatile.Read(ref _isSyncingModeConfigFromSession) != 0)
                return;

            _applyModeConfigToSession(config);
        }

        private void SetModeTitleTextSafe(string? text)
        {
#if UNITY_INCLUDE_TESTS || UNITY_EDITOR
            if (_isPlayerLoopDisabledForTests())
            {
                _modeTitleText.Value = text ?? string.Empty;
                return;
            }
#endif

            if (PlayerLoopHelper.IsMainThread)
            {
                _modeTitleText.Value = text ?? string.Empty;
                return;
            }

            SetModeTitleTextOnMainThreadAsync(text).Forget(GameLog.Exception);
        }

        private async UniTask SetModeTitleTextOnMainThreadAsync(string? text)
        {
            await UniTask.SwitchToMainThread();

            if (_isDisposed())
                return;

            _modeTitleText.Value = text ?? string.Empty;
        }

        private void ReleaseActiveSettings()
        {
            _activeConfigSubscription?.Dispose();
            _activeConfigSubscription = null;

            if (_activeSettingsViewModel != null)
            {
                try
                {
                    _activeSettingsViewModel.Dispose();
                }
                catch (Exception ex)
                {
                    GameLog.Exception(ex);
                }
                finally
                {
                    _activeSettingsViewModel = null;
                }
            }

            _activeSettings.Value = null;
            _updateCanStart();
        }

        private void DisposeModeTitleSubscription()
        {
            _modeTitleSubscription?.Dispose();
            _modeTitleSubscription = null;
        }

        private static TextTableId GetTableIdFromQualifiedKey(string qualifiedKey)
        {
            if (string.IsNullOrWhiteSpace(qualifiedKey))
                return new TextTableId("GameWizard");

            var dotIndex = qualifiedKey.IndexOf('.');
            return dotIndex <= 0 ? new TextTableId("GameWizard") : new TextTableId(qualifiedKey[..dotIndex]);
        }
    }
}