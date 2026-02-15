using System;
using R3;
using Runtime.Games.TicTacToe.Ultimate.Rules;
using UnityEngine.UIElements;

namespace Runtime.Games.TicTacToe.Ultimate.UI
{
    public sealed class UltimateAllowedBinder : IDisposable
    {
        private const string AllowedClass = "mini-board--allowed";

        private readonly IUltimateGameplayFieldUiAdapter _ui;
        private readonly IUltimateGameplayEventStream _events;
        private readonly IUltimateGameplaySnapshotProvider _snapshot;

        private CompositeDisposable _subscriptions;
        private bool _isBound;
        private bool _disposed;
        private ulong _epochAtBind;
        private AllowedMajors _currentAllowed;

        public UltimateAllowedBinder(
            IUltimateGameplayFieldUiAdapter ui,
            IUltimateGameplayEventStream events,
            IUltimateGameplaySnapshotProvider snapshot)
        {
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            _events = events ?? throw new ArgumentNullException(nameof(events));
            _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        }

        public void Bind()
        {
            ThrowIfDisposed();
            if (_isBound)
                return;

            _epochAtBind = _snapshot.Epoch;
            var initialAllowed = _snapshot.CurrentAllowedMajors;
            _currentAllowed = AllowedMajors.None;
            ApplyAllowed(initialAllowed);
            _currentAllowed = initialAllowed;

            _subscriptions = new CompositeDisposable();
            _events.AllowedMajorsChanged
                .Subscribe(OnAllowedMajorsChanged)
                .AddTo(_subscriptions);

            _isBound = true;
        }

        public void ApplyFinalState(AllowedMajors allowedMajors)
        {
            if (_disposed)
                return;

            ApplyAllowed(allowedMajors);
            _currentAllowed = allowedMajors;
        }

        public void Unbind()
        {
            if (!_isBound)
                return;

            _subscriptions?.Dispose();
            _subscriptions = null;
            _isBound = false;
        }

        private void OnAllowedMajorsChanged(AllowedMajorsChangedEvent evt)
        {
            if (!_isBound || evt.Epoch != _epochAtBind)
                return;

            ApplyAllowed(evt.AllowedMajors);
            _currentAllowed = evt.AllowedMajors;
        }

        private void ApplyAllowed(AllowedMajors nextAllowed)
        {
            for (var major = 0; major < 9; major++)
            {
                var had = _currentAllowed.ContainsMajor(major);
                var has = nextAllowed.ContainsMajor(major);
                if (had == has)
                    continue;

                if (!_ui.TryGetMiniBoard(major, out var mini) || mini == null)
                    continue;

                ToggleClass(mini, AllowedClass, has);
            }
        }

        private static void ToggleClass(VisualElement element, string className, bool enabled)
        {
            if (enabled)
                element.AddToClassList(className);
            else
                element.RemoveFromClassList(className);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(UltimateAllowedBinder));
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Unbind();
        }
    }
}
