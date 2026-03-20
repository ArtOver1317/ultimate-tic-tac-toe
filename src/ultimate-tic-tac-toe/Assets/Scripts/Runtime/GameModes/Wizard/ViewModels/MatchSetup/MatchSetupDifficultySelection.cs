#nullable enable

using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.Session;
using Runtime.Infrastructure.Logging;
using Runtime.Localization;
using Runtime.Localization.Contracts;
using Runtime.Localization.Types;
using Runtime.UI.Components;

namespace Runtime.GameModes.Wizard.ViewModels.MatchSetup
{
    internal sealed class MatchSetupDifficultySelection : IDisposable
    {
        private readonly ILocalizationService _localization;
        private readonly ReadOnlyReactiveProperty<IReadOnlyList<BotDifficulty>> _availableDifficulties;
        private readonly ReadOnlyReactiveProperty<OpponentType> _opponentType;
        private readonly Func<IGameSession?> _getSession;
        private readonly Func<string?> _getActiveModeId;
        private readonly Action<string> _logDisposedOnce;
        private readonly Func<bool> _isDisposed;
        private readonly Func<bool> _isPlayerLoopDisabledForTests;

        private readonly ReactiveProperty<IReadOnlyList<DifficultyChipItem>> _difficultyItems = new(Array.Empty<DifficultyChipItem>());
        private readonly ReactiveProperty<string?> _selectedDifficultyId = new(null);
        private readonly ReactiveProperty<bool> _isBotSettingsVisible = new(true);
        private readonly Dictionary<string, string> _difficultyLabels = new(StringComparer.Ordinal);

        private CompositeDisposable? _difficultyLocalizationSubscriptions;

        public MatchSetupDifficultySelection(
            ILocalizationService localization,
            ReadOnlyReactiveProperty<IReadOnlyList<BotDifficulty>> availableDifficulties,
            ReadOnlyReactiveProperty<OpponentType> opponentType,
            Func<IGameSession?> getSession,
            Func<string?> getActiveModeId,
            Action<string> logDisposedOnce,
            Func<bool> isDisposed,
            Func<bool> isPlayerLoopDisabledForTests)
        {
            _localization = localization ?? throw new ArgumentNullException(nameof(localization));
            _availableDifficulties = availableDifficulties ?? throw new ArgumentNullException(nameof(availableDifficulties));
            _opponentType = opponentType ?? throw new ArgumentNullException(nameof(opponentType));
            _getSession = getSession ?? throw new ArgumentNullException(nameof(getSession));
            _getActiveModeId = getActiveModeId ?? throw new ArgumentNullException(nameof(getActiveModeId));
            _logDisposedOnce = logDisposedOnce ?? throw new ArgumentNullException(nameof(logDisposedOnce));
            _isDisposed = isDisposed ?? throw new ArgumentNullException(nameof(isDisposed));
            _isPlayerLoopDisabledForTests = isPlayerLoopDisabledForTests ?? throw new ArgumentNullException(nameof(isPlayerLoopDisabledForTests));
        }

        public ReadOnlyReactiveProperty<IReadOnlyList<DifficultyChipItem>> DifficultyItems => _difficultyItems;
        public ReadOnlyReactiveProperty<string?> SelectedDifficultyId => _selectedDifficultyId;
        public ReadOnlyReactiveProperty<bool> IsBotSettingsVisible => _isBotSettingsVisible;

        public void Wire(Action<IDisposable> addDisposable)
        {
            if (addDisposable == null)
                throw new ArgumentNullException(nameof(addDisposable));

            addDisposable(_availableDifficulties.Subscribe(OnAvailableDifficultiesChanged));
            addDisposable(_opponentType.Subscribe(_ => UpdateBotSettingsVisibility()));
        }

        public void SetBotDifficultyId(string? difficultyId)
        {
            var normalized = string.IsNullOrWhiteSpace(difficultyId) ? null : difficultyId;

            if (!string.IsNullOrWhiteSpace(normalized) && !IsDifficultyAvailable(normalized))
                normalized = null;

            if (string.Equals(_selectedDifficultyId.CurrentValue, normalized, StringComparison.Ordinal))
                return;

            var session = _getSession();

            if (session == null)
            {
                GameLog.Warning("[MatchSetupViewModel] SetBotDifficultyId ignored: session not available.");
                return;
            }

            try
            {
                session.Update(snapshot =>
                    string.Equals(snapshot.BotDifficultyId, normalized, StringComparison.Ordinal)
                        ? snapshot
                        : snapshot.WithBotDifficultyId(normalized));
            }
            catch (ObjectDisposedException)
            {
                _logDisposedOnce("SetBotDifficultyId");
            }
        }

        public void ApplyBotDifficultyFromSession(string? difficultyId)
        {
            var normalized = NormalizeDifficultySelection(difficultyId, out var needsSanitize);

            if (!needsSanitize && string.Equals(_selectedDifficultyId.Value, normalized, StringComparison.Ordinal))
                return;

            _selectedDifficultyId.Value = normalized;

            if (!needsSanitize || _opponentType.CurrentValue != OpponentType.Bot)
                return;

            TryClearBotDifficultyInSession("ApplyBotDifficultyFromSession");
        }

        public bool IsDifficultyAvailable(string difficultyId)
        {
            var difficulties = _availableDifficulties.CurrentValue;

            foreach (var difficulty in difficulties)
            {
                if (string.Equals(difficulty.Id, difficultyId, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        public void Reset()
        {
            DisposeDifficultyLocalizationSubscriptions();
            _difficultyLabels.Clear();
            _difficultyItems.Value = Array.Empty<DifficultyChipItem>();
            _selectedDifficultyId.Value = null;
            _isBotSettingsVisible.Value = true;
        }

        public void Dispose()
        {
            Reset();
            _difficultyItems.Dispose();
            _selectedDifficultyId.Dispose();
            _isBotSettingsVisible.Dispose();
        }

#if UNITY_INCLUDE_TESTS || UNITY_EDITOR
        internal void SetDifficultyItemsForTests(IReadOnlyList<DifficultyChipItem>? items) =>
            _difficultyItems.Value = items ?? Array.Empty<DifficultyChipItem>();
#endif

        private string? NormalizeDifficultySelection(string? difficultyId, out bool needsSanitize)
        {
            var normalized = string.IsNullOrWhiteSpace(difficultyId) ? null : difficultyId;
            needsSanitize = !string.IsNullOrWhiteSpace(normalized) && !IsDifficultyAvailable(normalized);
            return needsSanitize ? null : normalized;
        }

        private void TryClearBotDifficultyInSession(string context)
        {
            var session = _getSession();

            if (session == null)
                return;

            try
            {
                session.Update(snapshot =>
                    string.IsNullOrWhiteSpace(snapshot.BotDifficultyId)
                        ? snapshot
                        : snapshot.WithBotDifficultyId(null));
            }
            catch (ObjectDisposedException)
            {
                _logDisposedOnce(context);
            }
        }

        private void OnAvailableDifficultiesChanged(IReadOnlyList<BotDifficulty> difficulties)
        {
#if UNITY_INCLUDE_TESTS || UNITY_EDITOR
            if (_isPlayerLoopDisabledForTests())
            {
                OnAvailableDifficultiesChangedCore(difficulties);
                return;
            }
#endif

            if (!PlayerLoopHelper.IsMainThread)
            {
                ApplyAvailableDifficultiesOnMainThreadAsync(difficulties).Forget(GameLog.Exception);
                return;
            }

            OnAvailableDifficultiesChangedCore(difficulties);
        }

        private void OnAvailableDifficultiesChangedCore(IReadOnlyList<BotDifficulty>? difficulties)
        {
            ResetDifficultyLocalizationState();
            NormalizeSelectedDifficultyAgainstAvailableSet(difficulties);

            if (difficulties == null || difficulties.Count == 0)
            {
                _difficultyItems.Value = Array.Empty<DifficultyChipItem>();
                UpdateBotSettingsVisibility();
                return;
            }

            SubscribeToDifficultyLabels(difficulties);
            UpdateDifficultyItems();
            UpdateBotSettingsVisibility();
        }

        private void ResetDifficultyLocalizationState()
        {
            DisposeDifficultyLocalizationSubscriptions();
            _difficultyLabels.Clear();
        }

        private void NormalizeSelectedDifficultyAgainstAvailableSet(IReadOnlyList<BotDifficulty>? difficulties)
        {
            var selectedId = _selectedDifficultyId.Value;

            if (string.IsNullOrWhiteSpace(selectedId) || ContainsDifficultyId(difficulties, selectedId))
                return;

            var session = _getSession();

            if (session == null)
            {
                _selectedDifficultyId.Value = null;
                GameLog.Warning("[MatchSetupViewModel] Session not available while normalizing difficulty selection.");
                return;
            }

            try
            {
                session.Update(snapshot =>
                    string.IsNullOrWhiteSpace(snapshot.BotDifficultyId)
                        ? snapshot
                        : snapshot.WithBotDifficultyId(null));
            }
            catch (ObjectDisposedException)
            {
                _selectedDifficultyId.Value = null;
                _logDisposedOnce("OnAvailableDifficultiesChanged");
                GameLog.Warning("[MatchSetupViewModel] Session disposed while normalizing difficulty selection.");
            }
        }

        private void SubscribeToDifficultyLabels(IReadOnlyList<BotDifficulty> difficulties)
        {
            var disposables = new CompositeDisposable();
            var table = new TextTableId("GameWizard");

            foreach (var difficulty in difficulties)
            {
                if (difficulty == null)
                    throw new InvalidOperationException("Difficulty catalog returned null item.");

                SubscribeToDifficultyLabel(table, difficulty, disposables);
            }

            _difficultyLocalizationSubscriptions = disposables;
        }

        private void SubscribeToDifficultyLabel(TextTableId table, BotDifficulty difficulty, CompositeDisposable disposables)
        {
            var currentDifficulty = difficulty;

            _localization
                .Observe(table, new TextKey(currentDifficulty.NameKey))
                .Subscribe(text => SetDifficultyLabelSafe(currentDifficulty.Id, text))
                .AddTo(disposables);
        }

        private void SetDifficultyLabelSafe(string difficultyId, string? text)
        {
#if UNITY_INCLUDE_TESTS || UNITY_EDITOR
            if (_isPlayerLoopDisabledForTests())
            {
                ApplyDifficultyLabel(difficultyId, text);
                UpdateDifficultyItems();
                return;
            }
#endif

            if (PlayerLoopHelper.IsMainThread)
            {
                ApplyDifficultyLabel(difficultyId, text);
                UpdateDifficultyItems();
                return;
            }

            SetDifficultyLabelOnMainThreadAsync(difficultyId, text).Forget(GameLog.Exception);
        }

        private void ApplyDifficultyLabel(string difficultyId, string? text) =>
            _difficultyLabels[difficultyId] = text ?? string.Empty;

        private async UniTask SetDifficultyLabelOnMainThreadAsync(string difficultyId, string? text)
        {
            await UniTask.SwitchToMainThread();

            if (_isDisposed())
                return;

            ApplyDifficultyLabel(difficultyId, text);
            UpdateDifficultyItems();
        }

        private void UpdateDifficultyItems() =>
            _difficultyItems.Value = BuildDifficultyItems(_availableDifficulties.CurrentValue);

        private IReadOnlyList<DifficultyChipItem> BuildDifficultyItems(IReadOnlyList<BotDifficulty>? difficulties)
        {
            if (difficulties == null || difficulties.Count == 0)
                return Array.Empty<DifficultyChipItem>();

            var items = new DifficultyChipItem[difficulties.Count];

            for (var i = 0; i < difficulties.Count; i++)
            {
                var difficulty = difficulties[i];

                if (difficulty == null)
                    throw new InvalidOperationException("Difficulty catalog returned null item.");

                _difficultyLabels.TryGetValue(difficulty.Id, out var label);
                items[i] = new DifficultyChipItem(difficulty.Id, label ?? string.Empty);
            }

            return Array.AsReadOnly(items);
        }

        private async UniTask ApplyAvailableDifficultiesOnMainThreadAsync(IReadOnlyList<BotDifficulty> difficulties)
        {
#if UNITY_INCLUDE_TESTS || UNITY_EDITOR
            if (_isPlayerLoopDisabledForTests())
            {
                OnAvailableDifficultiesChangedCore(difficulties);
                return;
            }
#endif

            await UniTask.SwitchToMainThread();

            if (_isDisposed())
                return;

            OnAvailableDifficultiesChangedCore(difficulties);
        }

        private void UpdateBotSettingsVisibility()
        {
            var hideDifficulty = MatchSetupBattleshipModeRules.ShouldHideBotDifficulty(
                _getActiveModeId(),
                _availableDifficulties.CurrentValue.Count);

            _isBotSettingsVisible.Value = _opponentType.CurrentValue == OpponentType.Bot && !hideDifficulty;
        }

        private void DisposeDifficultyLocalizationSubscriptions()
        {
            _difficultyLocalizationSubscriptions?.Dispose();
            _difficultyLocalizationSubscriptions = null;
        }

        private static bool ContainsDifficultyId(IReadOnlyList<BotDifficulty>? difficulties, string difficultyId)
        {
            if (difficulties == null || difficulties.Count == 0)
                return false;

            foreach (var difficulty in difficulties)
            {
                if (difficulty != null && string.Equals(difficulty.Id, difficultyId, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }
    }
}