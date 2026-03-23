using System;
using R3;
using Runtime.Games.TicTacToe.Ultimate.Rules;
using UnityEngine.UIElements;

namespace Runtime.Games.TicTacToe.Ultimate.UI
{
    public sealed class UltimateMiniBoardStatusBinder : IDisposable
    {
        private const string _statusOverlayName = "MiniStatusOverlay";
        private const string _closedClass = "mini-board--closed";
        private const string _wonByXClass = "mini-board--won-x";
        private const string _wonByOClass = "mini-board--won-o";
        private const string _drawClass = "mini-board--draw";
        private const string _overlayX = "X";
        private const string _overlayO = "O";
        private const string _overlayDraw = "=";
        private const string _winBounceClass = "mini-board--win-bounce";
        private const long _winBounceDurationMs = 280;

        private readonly IUltimateGameplayFieldUiAdapter _ui;
        private readonly IUltimateGameplayEventStream _events;
        private readonly IUltimateGameplaySnapshotProvider _snapshot;

        private readonly MiniBoardStatus[] _statuses = new MiniBoardStatus[UltimateBoardConstants.MajorCount];
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

            for (var major = 0; major < UltimateBoardConstants.MajorCount; major++)
            {
                ApplyMiniBoardVisual(major, _statuses[major]);
            }

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

            for (var major = 0; major < UltimateBoardConstants.MajorCount; major++)
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

            if (evt.Major is < 0 or >= UltimateBoardConstants.MajorCount)
                return;

            var oldStatus = _statuses[evt.Major];
            _statuses[evt.Major] = evt.NewStatus;
            ApplyMiniBoardVisual(evt.Major, evt.NewStatus);

            if (oldStatus == MiniBoardStatus.InProgress && evt.NewStatus != MiniBoardStatus.InProgress)
                TriggerWinBounce(evt.Major);
        }

        private void TriggerWinBounce(int major)
        {
            if (!_ui.TryGetMiniBoard(major, out var mini) || mini == null)
                return;

            mini.AddToClassList(_winBounceClass);
            mini.schedule.Execute(() => mini.RemoveFromClassList(_winBounceClass)).ExecuteLater(_winBounceDurationMs);
        }

        private void ApplyMiniBoardVisual(int major, MiniBoardStatus status)
        {
            if (!_ui.TryGetMiniBoard(major, out var mini) || mini == null)
                return;

            mini.RemoveFromClassList(_wonByXClass);
            mini.RemoveFromClassList(_wonByOClass);
            mini.RemoveFromClassList(_drawClass);

            var isClosed = status != MiniBoardStatus.InProgress;
            UltimateUiHelpers.ToggleClass(mini, _closedClass, isClosed);

            mini.SetEnabled(!isClosed);
            mini.pickingMode = isClosed ? PickingMode.Ignore : PickingMode.Position;

            if (status == MiniBoardStatus.WonByX)
                mini.AddToClassList(_wonByXClass);
            else if (status == MiniBoardStatus.WonByO)
                mini.AddToClassList(_wonByOClass);
            else if (status == MiniBoardStatus.Draw)
                mini.AddToClassList(_drawClass);

            ApplyStatusOverlay(mini, status);
        }

        private static void ApplyStatusOverlay(VisualElement mini, MiniBoardStatus status)
        {
            var overlay = mini.Q<Label>(_statusOverlayName);
            
            if (overlay == null)
                return;

            switch (status)
            {
                case MiniBoardStatus.WonByX:
                    overlay.text = _overlayX;
                    overlay.style.display = DisplayStyle.Flex;
                    break;
                case MiniBoardStatus.WonByO:
                    overlay.text = _overlayO;
                    overlay.style.display = DisplayStyle.Flex;
                    break;
                case MiniBoardStatus.Draw:
                    overlay.text = _overlayDraw;
                    overlay.style.display = DisplayStyle.Flex;
                    break;
                default:
                    overlay.text = string.Empty;
                    overlay.style.display = DisplayStyle.None;
                    break;
            }
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
