#nullable enable

using System;
using System.Collections.Generic;
using R3;
using Runtime.Infrastructure.Logging;
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

        private readonly ReactiveProperty<IReadOnlyList<GameModeMetadata>> _availableModes;
        private readonly ReactiveProperty<string?> _selectedModeId = new(null);
        private readonly ReactiveProperty<bool> _canContinue = new(false);

        private int _isWired;
        private int _isSyncingFromSession;

        public ReadOnlyReactiveProperty<IReadOnlyList<GameModeMetadata>> AvailableModes => _availableModes;
        public ReactiveProperty<string?> SelectedModeId => _selectedModeId;
        public ReadOnlyReactiveProperty<bool> CanContinue => _canContinue;

        public ModeSelectionViewModel(IGameModeCatalog catalog, IGameModeWizardCoordinator coordinator)
        {
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));
            _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));

            _availableModes = new ReactiveProperty<IReadOnlyList<GameModeMetadata>>(
                catalog.Metadata ?? throw new ArgumentException("Catalog returned null Metadata.", nameof(catalog)));

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

        protected override void OnReset()
        {
            // BaseViewModel.Reset() clears CompositeDisposable.
            // IMPORTANT (pooling-safe): do NOT (re)subscribe here.
            // Reset() is called when returning VM to pool.

            System.Threading.Volatile.Write(ref _isWired, 0);
            System.Threading.Volatile.Write(ref _isSyncingFromSession, 0);

            _selectedModeId.Value = null;
            _canContinue.Value = false;
        }

        protected override void OnDispose()
        {
            _availableModes.Dispose();
            _selectedModeId.Dispose();
            _canContinue.Dispose();
            base.OnDispose();
        }

        private void EnsureWired()
        {
            if (IsDisposed)
                return;

            if (System.Threading.Interlocked.Exchange(ref _isWired, 1) != 0)
                return;

            if (_coordinator.TryGetSession(out var session))
            {
                // Session -> VM
                AddDisposable(session.Snapshot
                    .SelectDistinct(s => s.SelectedModeId, StringComparer.Ordinal)
                    .Subscribe(ApplySelectionFromSession));

                // VM -> Session (write-through)
                AddDisposable(_selectedModeId
                    .Subscribe(id => OnSelectedModeChanged(id, session)));
            }
            else
            {
                // Coordinator not ready: keep UI disabled.
                AddDisposable(_selectedModeId.Subscribe(UpdateCanContinue));
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

            var currentId = session.Snapshot.CurrentValue?.SelectedModeId;
            if (string.Equals(currentId, selectedModeId, StringComparison.Ordinal))
                return;

            session.Update(s =>
                string.Equals(s.SelectedModeId, selectedModeId, StringComparison.Ordinal)
                    ? s
                    : s.WithSelectedModeId(selectedModeId));
        }

        private void UpdateCanContinue(string? selectedModeId) =>
            _canContinue.Value = !string.IsNullOrWhiteSpace(selectedModeId);
    }
}

#nullable restore
