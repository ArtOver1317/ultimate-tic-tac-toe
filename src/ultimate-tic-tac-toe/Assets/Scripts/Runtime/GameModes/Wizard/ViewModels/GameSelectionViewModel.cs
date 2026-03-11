#nullable enable

using System;
using System.Collections.Generic;
using R3;
using Runtime.GameModes.Wizard.Modes;
using Runtime.GameModes.Wizard.Session;
using Runtime.Infrastructure.Logging;
using Runtime.Localization;
using Runtime.UI.Core;

namespace Runtime.GameModes.Wizard.ViewModels
{
    /// <summary>
    /// View-model for the first step of the game mode wizard: selecting the desired game mode.
    /// Publishes navigation intents to <see cref="IGameWizardCoordinator"/>.
    /// </summary>
    public sealed class GameSelectionViewModel : BaseViewModel
    {
        private readonly IGameWizardCoordinator _coordinator;

        private readonly ReactiveProperty<IReadOnlyList<GameMetadata>> _availableModes;
        private readonly ReactiveProperty<string?> _selectedGameId = new(null);
        private readonly ReactiveProperty<bool> _canContinue = new(false);
        private readonly ReactiveProperty<bool> _isBusy = new(false);

        private int _isWired;
        private int _isSyncingFromSession;

        public ReadOnlyReactiveProperty<IReadOnlyList<GameMetadata>> AvailableModes => _availableModes;
        public ReactiveProperty<string?> SelectedGameId => _selectedGameId;
        public ReadOnlyReactiveProperty<bool> CanContinue => _canContinue;
        public ReadOnlyReactiveProperty<bool> IsBusy => _isBusy;
        public ReadOnlyReactiveProperty<WizardError?> Error => _coordinator.CurrentError;

        public Observable<string> TitleText { get; }
        public Observable<string> CancelButtonText { get; }
        public Observable<string> ContinueButtonText { get; }

        internal void SetAvailableModesForTests(IReadOnlyList<GameMetadata> modes) =>
            _availableModes.Value = modes;

        public GameSelectionViewModel(
            IGameCatalog catalog,
            IGameWizardCoordinator coordinator,
            ILocalizationService localization)
        {
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));
            
            _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
            var localization1 = localization ?? throw new ArgumentNullException(nameof(localization));

            _availableModes = new ReactiveProperty<IReadOnlyList<GameMetadata>>(
                catalog.Metadata ?? throw new ArgumentException("Catalog returned null Metadata.", nameof(catalog)));

            var table = new TextTableId("GameWizard");
            TitleText = localization1.Observe(table, new TextKey("GameWizard.GameSelection.Title"));
            CancelButtonText = localization1.Observe(table, new TextKey("GameWizard.GameSelection.Cancel"));
            ContinueButtonText = localization1.Observe(table, new TextKey("GameWizard.GameSelection.Continue"));

            UpdateCanContinue(_selectedGameId.Value);
        }

        public override void Initialize()
        {
            base.Initialize();
            EnsureWired();
        }

        public void SelectMode(string? gameId)
        {
            var normalized = string.IsNullOrWhiteSpace(gameId) ? null : gameId;

            if (string.Equals(_selectedGameId.Value, normalized, StringComparison.Ordinal))
                return;

            _selectedGameId.Value = normalized;
        }

        public void RequestContinue()
        {
            if (!_canContinue.Value)
                return;

            if (!_coordinator.TryPublishIntent(WizardIntent.Continue))
                GameLog.Debug("[GameSelectionViewModel] Continue intent rejected.");
        }

        public void RequestCancel()
        {
            // Cancel is expected to be accepted even during busy state.
            if (!_coordinator.TryPublishIntent(WizardIntent.Cancel))
                GameLog.Debug("[GameSelectionViewModel] Cancel intent rejected.");
        }

        public void AcknowledgeError() => _coordinator.ClearCurrentError();

        protected override void OnReset()
        {
            // BaseViewModel.Reset() clears CompositeDisposable.
            // IMPORTANT (pooling-safe): do NOT (re)subscribe here.
            // Reset() is called when returning VM to pool.

            System.Threading.Volatile.Write(ref _isWired, 0);
            System.Threading.Volatile.Write(ref _isSyncingFromSession, 0);

            _selectedGameId.Value = null;
            _canContinue.Value = false;
            _isBusy.Value = false;
        }

        protected override void OnDispose()
        {
            _availableModes.Dispose();
            _selectedGameId.Dispose();
            _canContinue.Dispose();
            _isBusy.Dispose();
            base.OnDispose();
        }

        private void EnsureWired()
        {
            if (IsDisposed)
                return;

            if (System.Threading.Interlocked.Exchange(ref _isWired, 1) != 0)
                return;

            AddDisposable(_coordinator.IsTransitioning.CombineLatest(_coordinator.IsSubmitting,
                    static (isTransitioning, isSubmitting) => isTransitioning || isSubmitting)
                .Subscribe(isBusy => _isBusy.Value = isBusy));

            if (_coordinator.TryGetSession(out var session))
            {
                // Session -> VM
                AddDisposable(session.Snapshot
                    .Subscribe(new SessionSelectionObserver(ApplySelectionFromSession)));

                // VM -> Session (write-through)
                AddDisposable(_selectedGameId
                    .Subscribe(id => OnSelectedModeChanged(id, session)));

                AddDisposable(_availableModes
                    .Subscribe(NormalizeSelectionAgainstAvailableModes));
            }
            else
            {
                // Coordinator not ready: keep UI disabled.
                AddDisposable(_selectedGameId.Subscribe(UpdateCanContinue));
                
                AddDisposable(_availableModes
                    .Subscribe(NormalizeSelectionAgainstAvailableModes));
            }
        }

        private void ApplySelectionFromSession(string? selectedGameId)
        {
            var normalized = string.IsNullOrWhiteSpace(selectedGameId) ? null : selectedGameId;

            if (string.Equals(_selectedGameId.Value, normalized, StringComparison.Ordinal))
                return;

            System.Threading.Interlocked.Exchange(ref _isSyncingFromSession, 1);

            try
            {
                _selectedGameId.Value = normalized;
            }
            finally
            {
                System.Threading.Interlocked.Exchange(ref _isSyncingFromSession, 0);
            }
        }

        private void OnSelectedModeChanged(string? selectedGameId, IGameSession session)
        {
            UpdateCanContinue(selectedGameId);

            if (System.Threading.Volatile.Read(ref _isSyncingFromSession) != 0)
                return;

            string? currentId;
            
            try
            {
                currentId = session.Snapshot.CurrentValue?.SelectedGameId;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            
            if (string.Equals(currentId, selectedGameId, StringComparison.Ordinal))
                return;

            session.Update(s =>
            {
                var updated = string.Equals(s.SelectedGameId, selectedGameId, StringComparison.Ordinal)
                    ? s
                    : s.WithSelectedGameId(selectedGameId);

                if (!string.Equals(selectedGameId, UltimateTicTacToeStrategy.DefaultGameId, StringComparison.Ordinal))
                    return updated;

                if (updated.OpponentType != OpponentType.Human)
                    updated = updated.WithOpponentType(OpponentType.Human);

                return updated;
            });
        }

        private void UpdateCanContinue(string? selectedGameId) =>
            _canContinue.Value = !string.IsNullOrWhiteSpace(selectedGameId);

        private void NormalizeSelectionAgainstAvailableModes(IReadOnlyList<GameMetadata>? modes)
        {
            if (_selectedGameId.Value == null)
                return;

            var hasMatch = false;
            
            if (modes != null)
            {
                foreach (var mode in modes)
                {
                    if (string.Equals(mode.Id, _selectedGameId.Value, StringComparison.Ordinal))
                    {
                        hasMatch = true;
                        break;
                    }
                }
            }

            if (!hasMatch)
                _selectedGameId.Value = null;
        }

        private sealed class SessionSelectionObserver : Observer<GameSessionSnapshot>
        {
            private readonly Action<string?> _onNext;
            private bool _hasLast;
            private string? _last;

            public SessionSelectionObserver(Action<string?> onNext) =>
                _onNext = onNext ?? throw new ArgumentNullException(nameof(onNext));

            protected override void OnNextCore(GameSessionSnapshot? value)
            {
                if (value == null)
                    return;

                string? selected;
                
                try
                {
                    selected = value.SelectedGameId;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                if (_hasLast && string.Equals(_last, selected, StringComparison.Ordinal))
                    return;

                _hasLast = true;
                _last = selected;
                _onNext(selected);
            }

            protected override void OnErrorResumeCore(Exception error)
            {
                if (error is ObjectDisposedException)
                    return;

                GameLog.Error($"[GameSelectionViewModel] Session snapshot error: {error}");
            }

            protected override void OnCompletedCore(Result result) { }
        }
    }
}