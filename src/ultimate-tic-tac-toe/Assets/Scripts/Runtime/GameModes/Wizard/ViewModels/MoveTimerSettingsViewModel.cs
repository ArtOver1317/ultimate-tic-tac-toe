#nullable enable

using System;
using System.Collections.Generic;
using R3;
using Runtime.Extensions;
using Runtime.Infrastructure.Logging;
using Runtime.Localization;
using Runtime.UI.Components;

namespace Runtime.GameModes.Wizard
{
    public sealed class MoveTimerSettingsViewModel : IDisposable
    {
        private static readonly TextTableId _table = new("GameWizard");

        private readonly CompositeDisposable _disposables = new();
        private readonly IReadOnlyList<int> _presetSeconds;
        private readonly ReactiveProperty<IReadOnlyList<DifficultyChipItem>> _presetItems = new(Array.Empty<DifficultyChipItem>());
        private readonly ReactiveProperty<string?> _selectedPresetId = new(null);
        private readonly ReactiveProperty<int> _moveTimeLimitSeconds = new(0);

        private string _offLabel = "No limit";
        private string _secondsFormat = "{0}s";

        public Observable<string> TitleText { get; }
        public ReadOnlyReactiveProperty<IReadOnlyList<DifficultyChipItem>> PresetItems => _presetItems;
        public ReadOnlyReactiveProperty<string?> SelectedPresetId => _selectedPresetId;
        public ReadOnlyReactiveProperty<int> MoveTimeLimitSeconds => _moveTimeLimitSeconds;

        public MoveTimerSettingsViewModel(MoveTimerPresetsConfig presetsConfig, ILocalizationService localization)
        {
            if (presetsConfig == null)
                throw new ArgumentNullException(nameof(presetsConfig));

            if (localization == null)
                throw new ArgumentNullException(nameof(localization));

            _presetSeconds = presetsConfig.GetPresets();
            TitleText = localization.Observe(_table, new TextKey("GameWizard.Timer.Label"));

            localization
                .Observe(_table, new TextKey("GameWizard.Timer.Off"))
                .Subscribe(text =>
                {
                    _offLabel = string.IsNullOrWhiteSpace(text) ? "No limit" : text;
                    RebuildPresetItems();
                })
                .AddTo(_disposables);

            localization
                .Observe(_table, new TextKey("GameWizard.Timer.SecondsFormat"))
                .Subscribe(text =>
                {
                    _secondsFormat = string.IsNullOrWhiteSpace(text) ? "{0}s" : text;
                    RebuildPresetItems();
                })
                .AddTo(_disposables);

            RebuildPresetItems();
            SelectBySecondsInternal(_presetSeconds[0]);
        }

        public void SetSelectedPresetId(string? presetId)
        {
            if (string.IsNullOrWhiteSpace(presetId) || !int.TryParse(presetId, out var seconds))
                return;

            SelectBySecondsInternal(seconds);
        }

        public bool TryApplyConfig(int moveTimeLimitSeconds)
        {
            if (!ContainsPreset(moveTimeLimitSeconds))
            {
                GameLog.Warning($"[MoveTimerSettingsViewModel] Unsupported move timer value '{moveTimeLimitSeconds}' from session. Fallback to '{_presetSeconds[0]}'.");
                SelectBySecondsInternal(_presetSeconds[0]);
                return false;
            }

            SelectBySecondsInternal(moveTimeLimitSeconds);
            return true;
        }

        public void Dispose()
        {
            _disposables.Dispose();
            _presetItems.Dispose();
            _selectedPresetId.Dispose();
            _moveTimeLimitSeconds.Dispose();
        }

        private bool ContainsPreset(int seconds)
        {
            for (var i = 0; i < _presetSeconds.Count; i++)
            {
                if (_presetSeconds[i] == seconds)
                    return true;
            }

            return false;
        }

        private void SelectBySecondsInternal(int seconds)
        {
            if (!ContainsPreset(seconds))
                return;

            var nextId = seconds.ToString();

            if (_moveTimeLimitSeconds.Value == seconds && string.Equals(_selectedPresetId.Value, nextId, StringComparison.Ordinal))
                return;

            _moveTimeLimitSeconds.Value = seconds;
            _selectedPresetId.Value = nextId;
        }

        private void RebuildPresetItems()
        {
            var items = new DifficultyChipItem[_presetSeconds.Count];

            for (var i = 0; i < _presetSeconds.Count; i++)
            {
                var seconds = _presetSeconds[i];
                var label = seconds == 0
                    ? _offLabel
                    : string.Format(_secondsFormat, seconds);

                items[i] = new DifficultyChipItem(seconds.ToString(), label);
            }

            _presetItems.Value = items;
        }
    }
}