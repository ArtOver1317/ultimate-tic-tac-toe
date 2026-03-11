#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.GameModes.Wizard;
using Runtime.GameModes.Wizard.Modes;
using Runtime.GameModes.Wizard.Session;
using Runtime.Infrastructure.Logging;

namespace Runtime.GameModes.Wizard.ViewModels.MatchSetup
{
    internal sealed class MatchSetupSessionBinding
    {
        private static readonly string[] _inlineErrorPriority =
        {
            WizardFieldNames.SelectedGameId,
            WizardFieldNames.GameConfig,
            WizardFieldNames.BotDifficultyId,
            WizardFieldNames.Matchmaking,
            WizardFieldNames.GameCatalog,
        };

        private readonly IGameWizardCoordinator _coordinator;
        private readonly ReactiveProperty<OpponentType> _opponentType;
        private readonly ReactiveProperty<HumanOpponentKind> _humanOpponentKind;
        private readonly ReactiveProperty<bool> _canStart;
        private readonly ReactiveProperty<bool> _isBusy;
        private readonly ReactiveProperty<string?> _inlineErrorText;
        private readonly MatchSetupModePresentation _modePresentation;
        private readonly MatchSetupDifficultySelection _difficultySelection;
        private readonly MatchSetupInviteSessionField _inviteSessionField;
        private readonly MoveTimerSettingsViewModel _moveTimerSettings;
        private readonly Action<IDisposable> _addDisposable;
        private readonly Func<string, string> _resolveMessageKey;
        private readonly Action<string> _logDisposedOnce;
        private readonly Func<bool> _isDisposed;
        private readonly Func<bool> _isPlayerLoopDisabledForTests;

        private int _lastAppliedVersion = -1;
        private int _isSyncingMoveTimerFromSession;
        private bool _sessionCanStart;
        private string? _validationErrorText;
        private string? _coordinatorInlineErrorText;

        public MatchSetupSessionBinding(
            IGameWizardCoordinator coordinator,
            ReactiveProperty<OpponentType> opponentType,
            ReactiveProperty<HumanOpponentKind> humanOpponentKind,
            ReactiveProperty<bool> canStart,
            ReactiveProperty<bool> isBusy,
            ReactiveProperty<string?> inlineErrorText,
            MatchSetupModePresentation modePresentation,
            MatchSetupDifficultySelection difficultySelection,
            MatchSetupInviteSessionField inviteSessionField,
            MoveTimerSettingsViewModel moveTimerSettings,
            Action<IDisposable> addDisposable,
            Func<string, string> resolveMessageKey,
            Action<string> logDisposedOnce,
            Func<bool> isDisposed,
            Func<bool> isPlayerLoopDisabledForTests)
        {
            _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
            _opponentType = opponentType ?? throw new ArgumentNullException(nameof(opponentType));
            _humanOpponentKind = humanOpponentKind ?? throw new ArgumentNullException(nameof(humanOpponentKind));
            _canStart = canStart ?? throw new ArgumentNullException(nameof(canStart));
            _isBusy = isBusy ?? throw new ArgumentNullException(nameof(isBusy));
            _inlineErrorText = inlineErrorText ?? throw new ArgumentNullException(nameof(inlineErrorText));
            _modePresentation = modePresentation ?? throw new ArgumentNullException(nameof(modePresentation));
            _difficultySelection = difficultySelection ?? throw new ArgumentNullException(nameof(difficultySelection));
            _inviteSessionField = inviteSessionField ?? throw new ArgumentNullException(nameof(inviteSessionField));
            _moveTimerSettings = moveTimerSettings ?? throw new ArgumentNullException(nameof(moveTimerSettings));
            _addDisposable = addDisposable ?? throw new ArgumentNullException(nameof(addDisposable));
            _resolveMessageKey = resolveMessageKey ?? throw new ArgumentNullException(nameof(resolveMessageKey));
            _logDisposedOnce = logDisposedOnce ?? throw new ArgumentNullException(nameof(logDisposedOnce));
            _isDisposed = isDisposed ?? throw new ArgumentNullException(nameof(isDisposed));
            _isPlayerLoopDisabledForTests = isPlayerLoopDisabledForTests ?? throw new ArgumentNullException(nameof(isPlayerLoopDisabledForTests));
        }

        public IGameSession? CurrentSession { get; private set; }

        public void Wire()
        {
            _addDisposable(_coordinator.IsTransitioning.CombineLatest(
                    _coordinator.IsSubmitting,
                    static (isTransitioning, isSubmitting) => isTransitioning || isSubmitting)
                .Subscribe(isBusy => _isBusy.Value = isBusy));
            
            _addDisposable(_coordinator.CurrentError.Subscribe(OnCoordinatorErrorChanged));
            _addDisposable(_moveTimerSettings.MoveTimeLimitSeconds.Skip(1).Subscribe(ApplyMoveTimeLimitToSession));

            if (!_coordinator.TryGetSession(out var session))
            {
                CurrentSession = null;
                return;
            }

            CurrentSession = session;
            _addDisposable(session.Snapshot
                .DistinctUntilVersionChanged()
                .Subscribe(new MatchSetupSessionSnapshotObserver(ApplySnapshot)));
            _addDisposable(session.CanStart.Subscribe(OnSessionCanStartChanged));
            _addDisposable(session.ValidationErrors.Subscribe(OnValidationErrorsChanged));
        }

        public void Reset()
        {
            CurrentSession = null;
            _lastAppliedVersion = -1;
            _sessionCanStart = false;
            _validationErrorText = null;
            _coordinatorInlineErrorText = null;
            Interlocked.Exchange(ref _isSyncingMoveTimerFromSession, 0);
            _isBusy.Value = false;
            UpdateCanStart();
            UpdateInlineError();
        }

        public void RefreshCanStart() => UpdateCanStart();

        private void ApplySnapshot(GameSessionSnapshot? snapshot)
        {
            if (snapshot == null)
                return;

            ApplyOnMainThread(snapshot, ApplySnapshotCore);
        }

        private void ApplySnapshotCore(GameSessionSnapshot snapshot)
        {
            if (snapshot.Version <= _lastAppliedVersion)
                return;

            _lastAppliedVersion = snapshot.Version;

            _modePresentation.ApplySelectedMode(snapshot.SelectedGameId, snapshot.GameConfig);
            ApplyOpponentTypeFromSession(snapshot.OpponentType);
            ApplyHumanOpponentKindFromSession(snapshot.HumanOpponentKind);
            _difficultySelection.ApplyBotDifficultyFromSession(snapshot.BotDifficultyId);
            _inviteSessionField.ApplyTargetPlayerIdFromSession(snapshot.TargetPlayerId);
            _modePresentation.ApplyModeConfigFromSession(snapshot.GameConfig);
            ApplyMoveTimeLimitFromSession(snapshot.MoveTimeLimitSeconds);
            ApplyModeSpecificConstraints(snapshot);
        }

        private void ApplyModeSpecificConstraints(GameSessionSnapshot snapshot)
        {
            var session = CurrentSession;

            if (session == null || !MatchSetupBattleshipModeRules.IsBattleshipGame(snapshot.SelectedGameId))
                return;

            if (TryNormalizeBattleshipHumanOpponentKind(session, snapshot))
                return;

            TryApplyBattleshipDefaultDifficulty(session, snapshot);
        }

        private bool TryNormalizeBattleshipHumanOpponentKind(IGameSession session, GameSessionSnapshot snapshot)
        {
            if (!MatchSetupBattleshipModeRules.RequiresDirectInvite(snapshot))
                return false;

            TryUpdateSession(
                session,
                current => !MatchSetupBattleshipModeRules.RequiresDirectInvite(current)
                    ? current
                    : current.WithHumanOpponentKind(HumanOpponentKind.DirectInvite),
                "ApplyModeSpecificConstraints.LocalHuman");

            return true;
        }

        private void TryApplyBattleshipDefaultDifficulty(IGameSession session, GameSessionSnapshot snapshot)
        {
            if (!MatchSetupBattleshipModeRules.ShouldApplyDefaultDifficulty(snapshot, _difficultySelection.IsDifficultyAvailable))
                return;

            TryUpdateSession(
                session,
                current => !MatchSetupBattleshipModeRules.ShouldApplyDefaultDifficulty(current, _difficultySelection.IsDifficultyAvailable)
                    ? current
                    : current.WithBotDifficultyId(BattleshipStrategy.DefaultBotDifficultyId),
                "ApplyModeSpecificConstraints.BotDifficulty");
        }

        private void ApplyOpponentTypeFromSession(OpponentType opponentType)
        {
            if (_opponentType.Value == opponentType)
                return;

            _opponentType.Value = opponentType;
        }

        private void ApplyHumanOpponentKindFromSession(HumanOpponentKind humanOpponentKind)
        {
            if (_humanOpponentKind.Value == humanOpponentKind)
                return;

            _humanOpponentKind.Value = humanOpponentKind;
        }

        private void OnSessionCanStartChanged(bool canStart)
            => ApplyOnMainThread(canStart, ApplyCanStartCore);

        private void ApplyCanStartCore(bool canStart)
        {
            _sessionCanStart = canStart;
            UpdateCanStart();
        }

        private void OnValidationErrorsChanged(IReadOnlyList<ValidationError> errors)
            => ApplyOnMainThread(errors, ApplyValidationErrorsCore);

        private void ApplyValidationErrorsCore(IReadOnlyList<ValidationError> errors)
        {
            _validationErrorText = BuildInlineErrorText(errors);
            _inviteSessionField.ApplyValidationErrors(errors);
            UpdateInlineError();
        }

        private void OnCoordinatorErrorChanged(WizardError? error)
            => ApplyOnMainThread(error, ApplyCoordinatorErrorCore);

        private void ApplyCoordinatorErrorCore(WizardError? error)
        {
            _coordinatorInlineErrorText = error is { DisplayType: ErrorDisplayType.Inline }
                ? _resolveMessageKey(error.MessageKey)
                : null;

            UpdateInlineError();
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
                        return _resolveMessageKey(candidate.MessageKey);
                }

                return null;
            }

            return _resolveMessageKey(bestError.MessageKey);
        }

        private static int GetInlineErrorPriority(string field)
        {
            if (string.IsNullOrWhiteSpace(field))
                return int.MaxValue;

            for (var index = 0; index < _inlineErrorPriority.Length; index++)
            {
                if (string.Equals(_inlineErrorPriority[index], field, StringComparison.Ordinal))
                    return index;
            }

            return int.MaxValue;
        }

        private void UpdateInlineError() =>
            _inlineErrorText.Value = _coordinatorInlineErrorText ?? _validationErrorText;

        private void UpdateCanStart() =>
            _canStart.Value = _sessionCanStart;

        private void ApplyMoveTimeLimitToSession(int seconds)
        {
            if (Volatile.Read(ref _isSyncingMoveTimerFromSession) != 0)
                return;

            TryUpdateSession(
                CurrentSession,
                snapshot => snapshot.MoveTimeLimitSeconds == seconds
                    ? snapshot
                    : snapshot.WithMoveTimeLimitSeconds(seconds),
                "ApplyMoveTimeLimitToSession");
        }

        private void ApplyMoveTimeLimitFromSession(int seconds)
        {
            Interlocked.Exchange(ref _isSyncingMoveTimerFromSession, 1);

            try
            {
                _moveTimerSettings.TryApplyConfig(seconds);
            }
            finally
            {
                Interlocked.Exchange(ref _isSyncingMoveTimerFromSession, 0);
            }
        }

        private void ApplyOnMainThread<T>(T value, Action<T> apply)
        {
#if UNITY_INCLUDE_TESTS || UNITY_EDITOR
            if (_isPlayerLoopDisabledForTests())
            {
                apply(value);
                return;
            }
#endif

            if (PlayerLoopHelper.IsMainThread)
            {
                apply(value);
                return;
            }

            ApplyOnMainThreadAsync(value, apply).Forget(GameLog.Exception);
        }

        private void TryUpdateSession(
            IGameSession? session,
            Func<GameSessionSnapshot, GameSessionSnapshot> update,
            string disposedContext)
        {
            if (session == null)
                return;

            try
            {
                session.Update(update);
            }
            catch (ObjectDisposedException)
            {
                _logDisposedOnce(disposedContext);
            }
        }

        private async UniTask ApplyOnMainThreadAsync<T>(T value, Action<T> apply)
        {
            await UniTask.SwitchToMainThread();

            if (_isDisposed())
                return;

            apply(value);
        }
    }
}
