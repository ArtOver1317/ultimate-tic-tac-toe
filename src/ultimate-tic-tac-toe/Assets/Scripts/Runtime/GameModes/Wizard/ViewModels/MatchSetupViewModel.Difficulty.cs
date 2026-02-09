#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.Infrastructure.Logging;
using Runtime.Localization;
using Runtime.UI.Components;

namespace Runtime.GameModes.Wizard
{
    /// <summary>
    /// Difficulty selection: localized chip labels, session sync, coalesced rebuilds.
    /// </summary>
    public sealed partial class MatchSetupViewModel
    {
        private void ApplyBotDifficultyFromSession(string? difficultyId)
        {
            var normalized = string.IsNullOrWhiteSpace(difficultyId) ? null : difficultyId;
            var needsSanitize = false;

            if (!string.IsNullOrWhiteSpace(normalized) && !IsDifficultyAvailable(normalized))
            {
                normalized = null;
                needsSanitize = true;
            }

            if (!needsSanitize && string.Equals(_selectedDifficultyId.Value, normalized, StringComparison.Ordinal))
                return;

            _selectedDifficultyId.Value = normalized;

            if (!needsSanitize)
                return;

            if (_opponentType.Value != global::Runtime.GameModes.Wizard.OpponentType.Bot)
                return;

            var session = _session;
            
            if (session == null)
                return;

            try
            {
                session.Update(s =>
                    string.IsNullOrWhiteSpace(s.BotDifficultyId)
                        ? s
                        : s.WithBotDifficultyId(null));
            }
            catch (ObjectDisposedException)
            {
                LogDisposedOnce("ApplyBotDifficultyFromSession");
            }
        }

        private void OnAvailableDifficultiesChanged(IReadOnlyList<BotDifficulty> difficulties)
        {
#if UNITY_INCLUDE_TESTS || UNITY_EDITOR
            if (_disablePlayerLoopForTests)
            {
                OnAvailableDifficultiesChangedCore(difficulties);
                return;
            }
#endif

            if (!PlayerLoopHelper.IsMainThread)
            {
                ApplyAvailableDifficultiesOnMainThreadAsync(difficulties).Forget(ex => GameLog.Exception(ex));
                return;
            }

            OnAvailableDifficultiesChangedCore(difficulties);
        }

        private void OnAvailableDifficultiesChangedCore(IReadOnlyList<BotDifficulty>? difficulties)
        {
            _difficultyLocalizationSubscriptions?.Dispose();
            _difficultyLocalizationSubscriptions = null;
            _difficultyLabels.Clear();

            var selectedId = _selectedDifficultyId.Value;
            
            if (!string.IsNullOrWhiteSpace(selectedId) && !ContainsDifficultyId(difficulties, selectedId))
            {
                var session = _session;
                
                if (session != null)
                {
                    try
                    {
                        session.Update(s =>
                            string.IsNullOrWhiteSpace(s.BotDifficultyId)
                                ? s
                                : s.WithBotDifficultyId(null));
                    }
                    catch (ObjectDisposedException)
                    {
                        _selectedDifficultyId.Value = null;
                        LogDisposedOnce("OnAvailableDifficultiesChanged");
                        GameLog.Warning("[MatchSetupViewModel] Session disposed while normalizing difficulty selection.");
                    }
                }
                else
                {
                    _selectedDifficultyId.Value = null;
                    GameLog.Warning("[MatchSetupViewModel] Session not available while normalizing difficulty selection.");
                }
            }

            if (difficulties == null || difficulties.Count == 0)
            {
                _difficultyItems.Value = Array.Empty<DifficultyChipItem>();
                return;
            }

            var disposables = new CompositeDisposable();
            var table = new TextTableId("GameWizard");

            foreach (var difficulty in difficulties)
            {
                if (difficulty == null)
                    throw new InvalidOperationException("Difficulty catalog returned null item.");

                var difficulty1 = difficulty;

                _localization
                    .Observe(table, new TextKey(difficulty.NameKey))
                    .Subscribe(text =>
                    {
                        SetDifficultyLabelSafe(difficulty1.Id, text);
                    })
                    .AddTo(disposables);
            }

            _difficultyLocalizationSubscriptions = disposables;
            RequestDifficultyItemsRebuild();
        }

        private void SetDifficultyLabelSafe(string difficultyId, string? text)
        {
#if UNITY_INCLUDE_TESTS || UNITY_EDITOR
            if (_disablePlayerLoopForTests)
            {
                _difficultyLabels[difficultyId] = text ?? string.Empty;
                UpdateDifficultyItems(_availableDifficulties.Value);
                return;
            }
#endif

            if (PlayerLoopHelper.IsMainThread)
            {
                _difficultyLabels[difficultyId] = text ?? string.Empty;
                RequestDifficultyItemsRebuild();
                return;
            }

            SetDifficultyLabelOnMainThreadAsync(difficultyId, text).Forget(ex => GameLog.Exception(ex));
        }

        private async UniTask SetDifficultyLabelOnMainThreadAsync(string difficultyId, string? text)
        {
            await UniTask.SwitchToMainThread();

            if (IsDisposed)
                return;

            _difficultyLabels[difficultyId] = text ?? string.Empty;
            RequestDifficultyItemsRebuild();
        }

        private void RequestDifficultyItemsRebuild()
        {
#if UNITY_INCLUDE_TESTS || UNITY_EDITOR
            if (_disablePlayerLoopForTests)
            {
                UpdateDifficultyItems(_availableDifficulties.Value);
                return;
            }
#endif

            Interlocked.Increment(ref _difficultyItemsRebuildVersion);

            if (Interlocked.Exchange(ref _difficultyItemsRebuildScheduled, 1) != 0)
                return;

            RebuildDifficultyItemsCoalescedAsync().Forget(ex => GameLog.Exception(ex));
        }

        private async UniTask RebuildDifficultyItemsCoalescedAsync()
        {
            await UniTask.SwitchToMainThread();

            if (IsDisposed)
            {
                Interlocked.Exchange(ref _difficultyItemsRebuildScheduled, 0);
                return;
            }

            var observedVersion = Volatile.Read(ref _difficultyItemsRebuildVersion);

            // Coalesce multiple localization emissions into a single rebuild per frame.
            await UniTask.Yield(PlayerLoopTiming.Update);

            if (IsDisposed)
            {
                Interlocked.Exchange(ref _difficultyItemsRebuildScheduled, 0);
                return;
            }

            UpdateDifficultyItems(_availableDifficulties.Value);

            Interlocked.Exchange(ref _difficultyItemsRebuildScheduled, 0);

            if (observedVersion != Volatile.Read(ref _difficultyItemsRebuildVersion))
                RequestDifficultyItemsRebuild();
        }

        private void UpdateDifficultyItems(IReadOnlyList<BotDifficulty>? difficulties)
        {
            if (difficulties == null || difficulties.Count == 0)
            {
                _difficultyItems.Value = Array.Empty<DifficultyChipItem>();
                return;
            }

            var items = new DifficultyChipItem[difficulties.Count];
            
            for (var i = 0; i < difficulties.Count; i++)
            {
                var difficulty = difficulties[i];
                
                if (difficulty == null)
                    throw new InvalidOperationException("Difficulty catalog returned null item.");

                _difficultyLabels.TryGetValue(difficulty.Id, out var label);
                items[i] = new DifficultyChipItem(difficulty.Id, label ?? string.Empty);
            }

            _difficultyItems.Value = Array.AsReadOnly(items);
        }

        private async UniTask ApplyAvailableDifficultiesOnMainThreadAsync(IReadOnlyList<BotDifficulty> difficulties)
        {
#if UNITY_INCLUDE_TESTS || UNITY_EDITOR
            if (_disablePlayerLoopForTests)
            {
                OnAvailableDifficultiesChangedCore(difficulties);
                return;
            }
#endif

            await UniTask.SwitchToMainThread();

            if (IsDisposed)
                return;

            OnAvailableDifficultiesChangedCore(difficulties);
        }

        private bool IsDifficultyAvailable(string difficultyId)
        {
            var difficulties = _availableDifficulties.Value;
            
            foreach (var t in difficulties)
            {
                if (string.Equals(t.Id, difficultyId, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static bool ContainsDifficultyId(IReadOnlyList<BotDifficulty>? difficulties, string difficultyId)
        {
            if (difficulties == null || difficulties.Count == 0)
                return false;

            for (var i = 0; i < difficulties.Count; i++)
            {
                if (difficulties[i] != null && string.Equals(difficulties[i].Id, difficultyId, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }
    }
}
