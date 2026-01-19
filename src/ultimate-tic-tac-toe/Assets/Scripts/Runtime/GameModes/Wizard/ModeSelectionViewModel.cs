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

            // Keep derived state consistent before Initialize() is called.
            _canContinue.Value = !string.IsNullOrWhiteSpace(_selectedModeId.Value);
        }

        public override void Initialize()
        {
            base.Initialize();
            EnsureWired();
        }

        public void SelectMode(string? modeId)
        {
            if (string.IsNullOrWhiteSpace(modeId))
            {
                _selectedModeId.Value = null;
                return;
            }

            _selectedModeId.Value = modeId;
        }

        public void RequestContinue()
        {
            if (string.IsNullOrWhiteSpace(_selectedModeId.Value))
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
                    .Subscribe(id =>
                    {
                        if (string.Equals(_selectedModeId.Value, id, StringComparison.Ordinal))
                            return;

                        System.Threading.Interlocked.Exchange(ref _isSyncingFromSession, 1);

                        try
                        {
                            _selectedModeId.Value = id;
                        }
                        finally
                        {
                            System.Threading.Interlocked.Exchange(ref _isSyncingFromSession, 0);
                        }
                    }));

                // VM -> Session (write-through)
                AddDisposable(_selectedModeId
                    .Subscribe(id =>
                    {
                        _canContinue.Value = !string.IsNullOrWhiteSpace(id);

                        if (System.Threading.Volatile.Read(ref _isSyncingFromSession) != 0)
                            return;

                        var currentId = session.Snapshot.CurrentValue?.SelectedModeId;
                        if (string.Equals(currentId, id, StringComparison.Ordinal))
                            return;

                        session.Update(s =>
                        {
                            if (string.Equals(s.SelectedModeId, id, StringComparison.Ordinal))
                                return s;

                            return s.WithSelectedModeId(id);
                        });
                    }));
            }
            else
            {
                // Coordinator not ready: keep UI disabled.
                AddDisposable(_selectedModeId.Subscribe(id => _canContinue.Value = !string.IsNullOrWhiteSpace(id)));
            }
        }
    }
}

#nullable restore
