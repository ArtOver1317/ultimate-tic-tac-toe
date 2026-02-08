#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.Infrastructure.Logging;
using Runtime.Localization;
using Runtime.UI.Core;

namespace Runtime.GameModes.Wizard
{
    /// <summary>
    /// Snapshot application, mode presentation, error handling, and thread marshalling.
    /// </summary>
    public sealed partial class MatchSetupViewModel
    {
        private void ApplySnapshot(GameModeSessionSnapshot? snapshot)
        {
            if (snapshot == null)
                return;

#if UNITY_INCLUDE_TESTS || UNITY_EDITOR
            if (_disablePlayerLoopForTests)
            {
                ApplySnapshotCore(snapshot);
                return;
            }
#endif

            if (!PlayerLoopHelper.IsMainThread)
            {
                ApplySnapshotOnMainThreadAsync(snapshot).Forget(ex => GameLog.Exception(ex));
                return;
            }

            ApplySnapshotCore(snapshot);
        }

        private void ApplySnapshotCore(GameModeSessionSnapshot? snapshot)
        {
            if (snapshot == null)
                return;

            if (snapshot.Version < _lastAppliedVersion)
                return;

            _lastAppliedVersion = snapshot.Version;
            _lastAppliedModeConfig = snapshot.ModeConfig;

            ApplySelectedMode(snapshot.SelectedModeId);
            ApplyOpponentTypeFromSession(snapshot.OpponentType);
            ApplyHumanOpponentKindFromSession(snapshot.HumanOpponentKind);
            ApplyBotDifficultyFromSession(snapshot.BotDifficultyId);
            ApplyTargetPlayerIdFromSession(snapshot.TargetPlayerId);
            ApplyModeConfigFromSession(snapshot.ModeConfig);
        }

        private void ApplySelectedMode(string? selectedModeId)
        {
            var normalized = string.IsNullOrWhiteSpace(selectedModeId) ? null : selectedModeId;

            if (string.Equals(_activeModeId, normalized, StringComparison.Ordinal))
                return;

            _activeModeId = normalized;
            UpdateModePresentation(normalized);
        }

        private void UpdateModePresentation(string? modeId)
        {
            ReleaseActiveSettings();

            if (string.IsNullOrWhiteSpace(modeId) 
                || !_catalog.TryGetStrategy(modeId, out var strategy) 
                || strategy == null)
            {
                _modeTitleSubscription?.Dispose();
                _modeTitleSubscription = null;
                _modeTitleText.Value = string.Empty;
                _modeIconKey.Value = string.Empty;
                _activeSettings.Value = null;
                UpdateCanStart();
                return;
            }

            _modeIconKey.Value = strategy.Metadata.IconAssetKey;

            _modeTitleSubscription?.Dispose();
            
            _modeTitleSubscription = _localization
                .Observe(new TextTableId("Mode"), new TextKey(strategy.Metadata.DisplayNameKey))
                .Subscribe(SetModeTitleTextSafe);

            var presentation = strategy.CreatePresentation();
            _activeSettings.Value = presentation;

            _activeSettingsViewModel = presentation.ViewModel;
            
            if (_activeSettingsViewModel is BaseViewModel baseViewModel)
                baseViewModel.Initialize();

            ApplyModeConfigFromSession(_lastAppliedModeConfig);

            _activeConfigSubscription = presentation.ViewModel.Config
                .Subscribe(ApplyModeConfig);
            
            ApplyModeConfig(presentation.ViewModel.Config.CurrentValue);
        }

        private void ApplyOpponentTypeFromSession(OpponentType opponentType)
        {
            if (_opponentType.Value == opponentType)
                return;

            _opponentType.Value = opponentType;
        }

        private void ApplyHumanOpponentKindFromSession(HumanOpponentKind kind)
        {
            if (_humanOpponentKind.Value == kind)
                return;

            _humanOpponentKind.Value = kind;

            if (kind != global::Runtime.GameModes.Wizard.HumanOpponentKind.DirectInvite)
                _playerIdErrorText.Value = null;
        }

        private void SetModeTitleTextSafe(string? text)
        {
#if UNITY_INCLUDE_TESTS || UNITY_EDITOR
            if (_disablePlayerLoopForTests)
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

            SetModeTitleTextOnMainThreadAsync(text).Forget(ex => GameLog.Exception(ex));
        }

        private async UniTask SetModeTitleTextOnMainThreadAsync(string? text)
        {
            await UniTask.SwitchToMainThread();

            if (IsDisposed)
                return;

            _modeTitleText.Value = text ?? string.Empty;
        }

        private void OnSessionCanStartChanged(bool canStart)
        {
#if UNITY_INCLUDE_TESTS || UNITY_EDITOR
            if (_disablePlayerLoopForTests)
            {
                _sessionCanStart = canStart;
                UpdateCanStart();
                return;
            }
#endif

            if (!PlayerLoopHelper.IsMainThread)
            {
                ApplyCanStartOnMainThreadAsync(canStart).Forget(ex => GameLog.Exception(ex));
                return;
            }

            _sessionCanStart = canStart;
            UpdateCanStart();
        }

        private void OnValidationErrorsChanged(IReadOnlyList<ValidationError> errors)
        {
#if UNITY_INCLUDE_TESTS || UNITY_EDITOR
            if (_disablePlayerLoopForTests)
            {
                ApplyValidationErrorsCore(errors);
                return;
            }
#endif

            if (!PlayerLoopHelper.IsMainThread)
            {
                ApplyValidationErrorsOnMainThreadAsync(errors).Forget(ex => GameLog.Exception(ex));
                return;
            }

            ApplyValidationErrorsCore(errors);
        }

        private void ApplyValidationErrorsCore(IReadOnlyList<ValidationError> errors)
        {
            _validationErrorText = BuildInlineErrorText(errors);
            _playerIdErrorText.Value = BuildPlayerIdErrorText(errors);
            UpdateInlineError();
        }

        private void OnCoordinatorErrorChanged(WizardError? error)
        {
#if UNITY_INCLUDE_TESTS || UNITY_EDITOR
            if (_disablePlayerLoopForTests)
            {
                ApplyCoordinatorErrorCore(error);
                return;
            }
#endif

            if (!PlayerLoopHelper.IsMainThread)
            {
                ApplyCoordinatorErrorOnMainThreadAsync(error).Forget(ex => GameLog.Exception(ex));
                return;
            }

            ApplyCoordinatorErrorCore(error);
        }

        private void ApplyCoordinatorErrorCore(WizardError? error)
        {
            _coordinatorInlineErrorText = error is { DisplayType: ErrorDisplayType.Inline }
                ? ResolveMessageKey(error.MessageKey)
                : null;

            UpdateInlineError();
        }

        private async UniTask ApplySnapshotOnMainThreadAsync(GameModeSessionSnapshot snapshot)
        {
            await UniTask.SwitchToMainThread();

            if (IsDisposed)
                return;

            if (snapshot.Version < _lastAppliedVersion)
                return;

            ApplySnapshotCore(snapshot);
        }

        private async UniTask ApplyCanStartOnMainThreadAsync(bool canStart)
        {
            await UniTask.SwitchToMainThread();

            if (IsDisposed)
                return;

            _sessionCanStart = canStart;
            UpdateCanStart();
        }

        private async UniTask ApplyValidationErrorsOnMainThreadAsync(IReadOnlyList<ValidationError> errors)
        {
            await UniTask.SwitchToMainThread();

            if (IsDisposed)
                return;

            ApplyValidationErrorsCore(errors);
        }

        private async UniTask ApplyCoordinatorErrorOnMainThreadAsync(WizardError? error)
        {
            await UniTask.SwitchToMainThread();

            if (IsDisposed)
                return;

            ApplyCoordinatorErrorCore(error);
        }

        private string? BuildInlineErrorText(IReadOnlyList<ValidationError>? errors)
        {
            if (errors == null || errors.Count == 0)
                return null;

            var bestError = errors[0];
            var bestPriority = GetInlineErrorPriority(bestError.Field);

            for (var i = 1; i < errors.Count; i++)
            {
                var candidate = errors[i];
                var candidatePriority = GetInlineErrorPriority(candidate.Field);
                
                if (candidatePriority < bestPriority)
                {
                    bestPriority = candidatePriority;
                    bestError = candidate;
                }
            }

            if (bestPriority == int.MaxValue)
            {
                foreach (var candidate in errors)
                {
                    if (!string.Equals(candidate.Field, WizardFieldNames.TargetPlayerId, StringComparison.Ordinal))
                        return ResolveMessageKey(candidate.MessageKey);
                }

                return null;
            }

            return ResolveMessageKey(bestError.MessageKey);
        }

        private static int GetInlineErrorPriority(string field)
        {
            if (string.IsNullOrWhiteSpace(field))
                return int.MaxValue;

            for (var i = 0; i < _inlineErrorPriority.Length; i++)
            {
                if (string.Equals(_inlineErrorPriority[i], field, StringComparison.Ordinal))
                    return i;
            }

            return int.MaxValue;
        }

        private string ResolveMessageKey(string messageKey)
        {
            if (string.IsNullOrWhiteSpace(messageKey))
                return string.Empty;

            var dotIndex = messageKey.IndexOf('.', StringComparison.Ordinal);
            
            if (dotIndex <= 0)
                return messageKey;

            var tableName = messageKey[..dotIndex];
            return _localization.Resolve(new TextTableId(tableName), new TextKey(messageKey));
        }

        private void UpdateInlineError() =>
            _inlineErrorText.Value = _coordinatorInlineErrorText ?? _validationErrorText;

        private void UpdateCanStart() =>
            _canStart.Value = _sessionCanStart;

        private void ApplyModeConfig(IGameModeConfig? config)
        {
            if (config == null)
                return;

            if (Volatile.Read(ref _isSyncingModeConfigFromSession) != 0)
                return;

            var session = _session;
            
            if (session == null)
                return;

            try
            {
                session.SetModeConfig(config);
            }
            catch (ObjectDisposedException)
            {
                LogDisposedOnce("ApplyModeConfig");
            }
        }

        private void ApplyModeConfigFromSession(IGameModeConfig? config)
        {
            if (config == null)
                return;

            var viewModel = _activeSettingsViewModel;
            
            if (viewModel == null)
                return;

            Interlocked.Exchange(ref _isSyncingModeConfigFromSession, 1);

            try
            {
                if (!viewModel.TryApplyConfig(config))
                    GameLog.Warning($"[MatchSetupViewModel] Mode config type mismatch for {viewModel.GetType().Name}.");
            }
            finally
            {
                Interlocked.Exchange(ref _isSyncingModeConfigFromSession, 0);
            }
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
            UpdateCanStart();
        }

        private void LogDisposedOnce(string context)
        {
            if (Interlocked.Exchange(ref _disposedExceptionLogged, 1) != 0)
                return;

            GameLog.Debug($"[MatchSetupViewModel] Ignored ObjectDisposedException in {context}.");
        }

        private sealed class SessionSnapshotObserver : Observer<GameModeSessionSnapshot>
        {
            private readonly Action<GameModeSessionSnapshot> _onNext;

            public SessionSnapshotObserver(Action<GameModeSessionSnapshot> onNext) =>
                _onNext = onNext ?? throw new ArgumentNullException(nameof(onNext));

            protected override void OnNextCore(GameModeSessionSnapshot? value)
            {
                if (value == null)
                    return;

                _onNext(value);
            }

            protected override void OnErrorResumeCore(Exception error)
            {
                if (error is ObjectDisposedException)
                    return;

                GameLog.Error($"[MatchSetupViewModel] Session snapshot error: {error}");
            }

            protected override void OnCompletedCore(Result result) { }
        }
    }
}
