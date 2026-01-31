#nullable enable

using System;
using System.Collections.Generic;
using R3;
using Runtime.Infrastructure.Logging;
using Runtime.Localization;
using Runtime.UI.Core;

namespace Runtime.GameModes.Wizard
{
    /// <summary>
    /// View-model for the first step of the game mode wizard: selecting the desired game mode.
    /// Publishes navigation intents to <see cref="IGameModeWizardCoordinator"/>.
    /// </summary>
    public sealed class ModeSelectionViewModel : BaseViewModel
    {
        private readonly IGameModeWizardCoordinator _coordinator;
        private readonly ILocalizationService _localization;

        private readonly ReactiveProperty<IReadOnlyList<GameModeMetadata>> _availableModes;
        private readonly ReactiveProperty<string?> _selectedModeId = new(null);
        private readonly ReactiveProperty<bool> _canContinue = new(false);
        private readonly ReactiveProperty<bool> _isBusy = new(false);

        private int _isWired;
        private int _isSyncingFromSession;

        public ReadOnlyReactiveProperty<IReadOnlyList<GameModeMetadata>> AvailableModes => _availableModes;
        public ReactiveProperty<string?> SelectedModeId => _selectedModeId;
        public ReadOnlyReactiveProperty<bool> CanContinue => _canContinue;
        public ReadOnlyReactiveProperty<bool> IsBusy => _isBusy;
        public ReadOnlyReactiveProperty<WizardError?> Error => _coordinator.CurrentError;

        public Observable<string> TitleText { get; }
        public Observable<string> CancelButtonText { get; }
        public Observable<string> ContinueButtonText { get; }

        internal void SetAvailableModesForTests(IReadOnlyList<GameModeMetadata> modes) =>
            _availableModes.Value = modes;

        public ModeSelectionViewModel(
            IGameModeCatalog catalog,
            IGameModeWizardCoordinator coordinator,
            ILocalizationService localization)
        {
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));
            _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
            _localization = localization ?? throw new ArgumentNullException(nameof(localization));

            _availableModes = new ReactiveProperty<IReadOnlyList<GameModeMetadata>>(
                catalog.Metadata ?? throw new ArgumentException("Catalog returned null Metadata.", nameof(catalog)));

            var table = new TextTableId("GameModeWizard");
            TitleText = _localization.Observe(table, new TextKey("GameModeWizard.ModeSelection.Title"));
            CancelButtonText = _localization.Observe(table, new TextKey("GameModeWizard.ModeSelection.Cancel"));
            ContinueButtonText = _localization.Observe(table, new TextKey("GameModeWizard.ModeSelection.Continue"));

            UpdateCanContinue(_selectedModeId.Value);
        }

        public override void Initialize()
        {
            base.Initialize();
            EnsureWired();
        }

        public void SelectMode(string? modeId)
        {
            var normalized = string.IsNullOrWhiteSpace(modeId) ? null : modeId;

            if (string.Equals(_selectedModeId.Value, normalized, StringComparison.Ordinal))
                return;

            _selectedModeId.Value = normalized;
        }

        public void RequestContinue()
        {
            if (!_canContinue.Value)
                return;

            if (!_coordinator.TryPublishIntent(WizardIntent.Continue))
                GameLog.Debug("[ModeSelectionViewModel] Continue intent rejected.");
        }

        public void RequestCancel()
        {
            // Cancel is expected to be accepted even during busy state.
            if (!_coordinator.TryPublishIntent(WizardIntent.Cancel))
                GameLog.Debug("[ModeSelectionViewModel] Cancel intent rejected.");
        }

        public void AcknowledgeError()
        {
            _coordinator.ClearCurrentError();
        }

        protected override void OnReset()
        {
            // BaseViewModel.Reset() clears CompositeDisposable.
            // IMPORTANT (pooling-safe): do NOT (re)subscribe here.
            // Reset() is called when returning VM to pool.

            System.Threading.Volatile.Write(ref _isWired, 0);
            System.Threading.Volatile.Write(ref _isSyncingFromSession, 0);

            _selectedModeId.Value = null;
            _canContinue.Value = false;
            _isBusy.Value = false;
        }

        protected override void OnDispose()
        {
            _availableModes.Dispose();
            _selectedModeId.Dispose();
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

            AddDisposable(Observable.CombineLatest(
                    _coordinator.IsTransitioning,
                    _coordinator.IsSubmitting,
                    static (isTransitioning, isSubmitting) => isTransitioning || isSubmitting)
                .Subscribe(isBusy => _isBusy.Value = isBusy));

            if (_coordinator.TryGetSession(out var session))
            {
                // Session -> VM
                AddDisposable(session.Snapshot
                    .Subscribe(new SessionSelectionObserver(ApplySelectionFromSession)));

                // VM -> Session (write-through)
                AddDisposable(_selectedModeId
                    .Subscribe(id => OnSelectedModeChanged(id, session)));

                AddDisposable(_availableModes
                    .Subscribe(modes => NormalizeSelectionAgainstAvailableModes(modes)));
            }
            else
            {
                // Coordinator not ready: keep UI disabled.
                AddDisposable(_selectedModeId.Subscribe(UpdateCanContinue));
                AddDisposable(_availableModes
                    .Subscribe(modes => NormalizeSelectionAgainstAvailableModes(modes)));
            }
        }

        private void ApplySelectionFromSession(string? selectedModeId)
        {
            var normalized = string.IsNullOrWhiteSpace(selectedModeId) ? null : selectedModeId;

            if (string.Equals(_selectedModeId.Value, normalized, StringComparison.Ordinal))
                return;

            System.Threading.Interlocked.Exchange(ref _isSyncingFromSession, 1);

            try
            {
                _selectedModeId.Value = normalized;
            }
            finally
            {
                System.Threading.Interlocked.Exchange(ref _isSyncingFromSession, 0);
            }
        }

        private void OnSelectedModeChanged(string? selectedModeId, IGameModeSession session)
        {
            UpdateCanContinue(selectedModeId);

            if (System.Threading.Volatile.Read(ref _isSyncingFromSession) != 0)
                return;

            string? currentId;
            try
            {
                currentId = session.Snapshot.CurrentValue?.SelectedModeId;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            if (string.Equals(currentId, selectedModeId, StringComparison.Ordinal))
                return;

            session.Update(s =>
                string.Equals(s.SelectedModeId, selectedModeId, StringComparison.Ordinal)
                    ? s
                    : s.WithSelectedModeId(selectedModeId));
        }

        private void UpdateCanContinue(string? selectedModeId) =>
            _canContinue.Value = !string.IsNullOrWhiteSpace(selectedModeId);

        private void NormalizeSelectionAgainstAvailableModes(IReadOnlyList<GameModeMetadata> modes)
        {
            if (_selectedModeId.Value == null)
                return;

            var hasMatch = false;
            if (modes != null)
            {
                for (var i = 0; i < modes.Count; i++)
                {
                    if (string.Equals(modes[i].Id, _selectedModeId.Value, StringComparison.Ordinal))
                    {
                        hasMatch = true;
                        break;
                    }
                }
            }

            if (!hasMatch)
                _selectedModeId.Value = null;
        }

        private sealed class SessionSelectionObserver : Observer<GameModeSessionSnapshot>
        {
            private readonly Action<string?> _onNext;
            private bool _hasLast;
            private string? _last;

            public SessionSelectionObserver(Action<string?> onNext) =>
                _onNext = onNext ?? throw new ArgumentNullException(nameof(onNext));

            protected override void OnNextCore(GameModeSessionSnapshot value)
            {
                if (value == null)
                    return;

                string? selected;
                try
                {
                    selected = value.SelectedModeId;
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

                GameLog.Error($"[ModeSelectionViewModel] Session snapshot error: {error}");
            }

            protected override void OnCompletedCore(Result result)
            {
            }
        }
    }
}

#nullable restore
