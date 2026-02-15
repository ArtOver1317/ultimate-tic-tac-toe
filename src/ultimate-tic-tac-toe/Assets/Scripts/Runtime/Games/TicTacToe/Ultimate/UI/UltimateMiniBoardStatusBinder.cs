using System;
using R3;
using Runtime.Games.TicTacToe.Ultimate.Rules;
using UnityEngine.UIElements;

namespace Runtime.Games.TicTacToe.Ultimate.UI
{
    public sealed class UltimateMiniBoardStatusBinder : IDisposable
    {
        private const string StatusOverlayName = "MiniStatusOverlay";
        private const string ClosedClass = "mini-board--closed";
        private const string WonByXClass = "mini-board--won-x";
        private const string WonByOClass = "mini-board--won-o";
        private const string DrawClass = "mini-board--draw";

        private readonly IUltimateGameplayFieldUiAdapter _ui;
        private readonly IUltimateGameplayEventStream _events;
        private readonly IUltimateGameplaySnapshotProvider _snapshot;

        private readonly MiniBoardStatus[] _statuses = new MiniBoardStatus[9];
        private CompositeDisposable _subscriptions;
        private bool _isBound;
        private bool _disposed;
        private ulong _epochAtBind;

        public UltimateMiniBoardStatusBinder(
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
            _snapshot.CopyMiniBoardsTo(_statuses);

            for (var major = 0; major < 9; major++)
                ApplyMiniBoardVisual(major, _statuses[major]);

            _subscriptions = new CompositeDisposable();
            _events.MiniBoardStatusChanged
                .Subscribe(OnMiniBoardStatusChanged)
                .AddTo(_subscriptions);

            _isBound = true;
        }

        public void ApplyFinalState(ReadOnlySpan<MiniBoardStatus> miniBoards)
        {
            if (_disposed)
                return;

            for (var major = 0; major < 9; major++)
            {
                _statuses[major] = miniBoards[major];
                ApplyMiniBoardVisual(major, _statuses[major]);
            }
        }

        public void Unbind()
        {
            if (!_isBound)
                return;

            _subscriptions?.Dispose();
            _subscriptions = null;
            _isBound = false;
        }

        private void OnMiniBoardStatusChanged(MiniBoardStatusChangedEvent evt)
        {
            if (!_isBound || evt.Epoch != _epochAtBind)
                return;

            if (evt.Major < 0 || evt.Major >= 9)
                return;

            _statuses[evt.Major] = evt.NewStatus;
            ApplyMiniBoardVisual(evt.Major, evt.NewStatus);
        }

        private void ApplyMiniBoardVisual(int major, MiniBoardStatus status)
        {
            if (!_ui.TryGetMiniBoard(major, out var mini) || mini == null)
                return;

            mini.RemoveFromClassList(WonByXClass);
            mini.RemoveFromClassList(WonByOClass);
            mini.RemoveFromClassList(DrawClass);

            var isClosed = status != MiniBoardStatus.InProgress;
            ToggleClass(mini, ClosedClass, isClosed);

            mini.SetEnabled(!isClosed);
            mini.pickingMode = isClosed ? PickingMode.Ignore : PickingMode.Position;

            if (status == MiniBoardStatus.WonByX)
                mini.AddToClassList(WonByXClass);
            else if (status == MiniBoardStatus.WonByO)
                mini.AddToClassList(WonByOClass);
            else if (status == MiniBoardStatus.Draw)
                mini.AddToClassList(DrawClass);

            ApplyStatusOverlay(mini, status);
        }

        private static void ApplyStatusOverlay(VisualElement mini, MiniBoardStatus status)
        {
            var overlay = mini.Q<Label>(StatusOverlayName);
            if (overlay == null)
                return;

            switch (status)
            {
                case MiniBoardStatus.WonByX:
                    overlay.text = "X";
                    overlay.style.display = DisplayStyle.Flex;
                    break;
                case MiniBoardStatus.WonByO:
                    overlay.text = "O";
                    overlay.style.display = DisplayStyle.Flex;
                    break;
                case MiniBoardStatus.Draw:
                    overlay.text = "=";
                    overlay.style.display = DisplayStyle.Flex;
                    break;
                default:
                    overlay.text = string.Empty;
                    overlay.style.display = DisplayStyle.None;
                    break;
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
                throw new ObjectDisposedException(nameof(UltimateMiniBoardStatusBinder));
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
