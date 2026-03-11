#nullable enable

using System;
using System.Collections.Generic;
using R3;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.Modes;
using Runtime.Localization;
using Runtime.UI.Components;
using Runtime.UI.Core;

namespace Runtime.GameModes.Wizard.ViewModels
{
    public interface IBattleshipSettingsViewModel : IGameSettingsViewModel
    {
        ReadOnlyReactiveProperty<IReadOnlyList<DifficultyChipItem>> PlacementTimerPresetItems { get; }
        ReadOnlyReactiveProperty<string?> SelectedPlacementTimerPresetId { get; }
        void SetSelectedPlacementTimerPresetId(string id);
    }

    public sealed class BattleshipSettingsViewModel : BaseViewModel, IBattleshipSettingsViewModel
    {
        private readonly CompositeDisposable _disposables = new();
        private readonly IReadOnlyList<int> _presetSeconds;

        private readonly ReactiveProperty<IReadOnlyList<DifficultyChipItem>> _placementTimerPresetItems = new(Array.Empty<DifficultyChipItem>());
        private readonly ReactiveProperty<string?> _selectedPlacementTimerPresetId = new(null);
        private readonly ReactiveProperty<IGameConfig> _config;
        private readonly ReactiveProperty<bool> _isValid = new(true);

        private string _offLabel = "No limit";
        private string _secondsFormat = "{0}s";

        public ReadOnlyReactiveProperty<IReadOnlyList<DifficultyChipItem>> PlacementTimerPresetItems => _placementTimerPresetItems;
        public ReadOnlyReactiveProperty<string?> SelectedPlacementTimerPresetId => _selectedPlacementTimerPresetId;
        public ReadOnlyReactiveProperty<IGameConfig> Config => _config;
        public ReadOnlyReactiveProperty<bool> IsValid => _isValid;

        public BattleshipSettingsViewModel(MoveTimerPresetsConfig presetsConfig, ILocalizationService localization)
        {
            if (presetsConfig == null)
                throw new ArgumentNullException(nameof(presetsConfig));

            if (localization == null)
                throw new ArgumentNullException(nameof(localization));

            _presetSeconds = presetsConfig.GetPresets();
            _config = new ReactiveProperty<IGameConfig>(new BattleshipConfig(_presetSeconds[0]));

            var table = new TextTableId("GameWizard");
            
            localization
                .Observe(table, new TextKey("GameWizard.Timer.Off"))
                .Subscribe(text =>
                {
                    _offLabel = string.IsNullOrWhiteSpace(text) ? "No limit" : text;
                    RebuildPresetItems();
                })
                .AddTo(_disposables);

            localization
                .Observe(table, new TextKey("GameWizard.Timer.SecondsFormat"))
                .Subscribe(text =>
                {
                    _secondsFormat = string.IsNullOrWhiteSpace(text) ? "{0}s" : text;
                    RebuildPresetItems();
                })
                .AddTo(_disposables);

            RebuildPresetItems();
            SelectBySecondsInternal(_presetSeconds[0]);
        }

        public void SetSelectedPlacementTimerPresetId(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || !int.TryParse(id, out var seconds))
                return;

            SelectBySecondsInternal(seconds);
        }

        public bool TryApplyConfig(IGameConfig config)
        {
            if (config is not BattleshipConfig battleshipConfig)
                return false;

            if (!ContainsPreset(battleshipConfig.PlacementTimeLimitSeconds))
                return false;

            SelectBySecondsInternal(battleshipConfig.PlacementTimeLimitSeconds);
            return true;
        }

        protected override void OnReset() => SelectBySecondsInternal(_presetSeconds[0]);

        protected override void OnDispose()
        {
            _disposables.Dispose();
            _placementTimerPresetItems.Dispose();
            _selectedPlacementTimerPresetId.Dispose();
            _config.Dispose();
            _isValid.Dispose();
            base.OnDispose();
        }

        private bool ContainsPreset(int seconds)
        {
            foreach (var preset in _presetSeconds)
            {
                if (preset == seconds)
                    return true;
            }

            return false;
        }

        private void SelectBySecondsInternal(int seconds)
        {
            if (!ContainsPreset(seconds))
                return;

            var id = seconds.ToString();

            if (string.Equals(_selectedPlacementTimerPresetId.Value, id, StringComparison.Ordinal)
                && _config.Value is BattleshipConfig cfg
                && cfg.PlacementTimeLimitSeconds == seconds)
                return;

            _selectedPlacementTimerPresetId.Value = id;
            _config.Value = new BattleshipConfig(seconds);
            _isValid.Value = true;
        }

        private void RebuildPresetItems()
        {
            var items = new DifficultyChipItem[_presetSeconds.Count];

            for (var i = 0; i < _presetSeconds.Count; i++)
            {
                var seconds = _presetSeconds[i];
                var label = seconds == 0 ? _offLabel : string.Format(_secondsFormat, seconds);
                items[i] = new DifficultyChipItem(seconds.ToString(), label);
            }

            _placementTimerPresetItems.Value = items;
        }
    }
}
