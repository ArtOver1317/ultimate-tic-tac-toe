#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Runtime.GameModes.Wizard.Online.Flow
{
    internal enum OnlineFlowEventKind
    {
        EnterHumanSetup,
        ConfirmHostIntent,
        StartHost,
        StartJoin,
        CopyVisibleSessionId,
        BackPressed,
        ExitPressed,
        SetReadyForNextMatch,
        OpponentReadyForNextMatch,
        HostCreated,
        JoinSucceeded,
        JoinFailed,
        GuestJoined,
        DisconnectDetected,
        ReconnectSucceeded,
        GraceTimeout,
        OpponentLeft,
        CountdownTick,
        GameplayEntered,
        RoundCompleted,
    }

    internal enum OnlineFlowEventPriority
    {
        High = 0,
        Normal = 1,
    }

    internal readonly struct OnlineFlowQueuedEvent
    {
        public OnlineFlowEventKind Event { get; }
        public OnlineFlowEventPriority Priority { get; }
        public long Sequence { get; }
        public int? Epoch { get; }
        public OnlineErrorCode ErrorCode { get; }
        public string? RawSessionIdInput { get; }
        public string? Region { get; }
        public bool? IsReady { get; }
        public int? CountdownRemainingSeconds { get; }

        public OnlineFlowQueuedEvent(
            OnlineFlowEventKind @event,
            OnlineFlowEventPriority priority,
            long sequence,
            int? epoch,
            OnlineErrorCode errorCode,
            string? rawSessionIdInput,
            string? region,
            bool? isReady,
            int? countdownRemainingSeconds)
        {
            Event = @event;
            Priority = priority;
            Sequence = sequence;
            Epoch = epoch;
            ErrorCode = errorCode;
            RawSessionIdInput = rawSessionIdInput;
            Region = region;
            IsReady = isReady;
            CountdownRemainingSeconds = countdownRemainingSeconds;
        }
    }

    internal sealed class OnlineFlowEventQueue : IDisposable
    {
        private readonly List<OnlineFlowQueuedEvent> _pendingEvents = new();
        private readonly object _eventLock = new();
        private readonly Action<OnlineFlowQueuedEvent> _applyEvent;

        private long _eventSequence;
        private bool _isDisposed;
        private bool _isProcessing;

        public OnlineFlowEventQueue(Action<OnlineFlowQueuedEvent> applyEvent) =>
            _applyEvent = applyEvent ?? throw new ArgumentNullException(nameof(applyEvent));

        public UniTask EnqueueAsync(
            OnlineFlowEventKind flowEvent,
            OnlineFlowEventPriority priority,
            OnlineErrorCode errorCode,
            int? epoch,
            string? rawSessionIdInput,
            string? region,
            bool? isReady,
            int? countdownRemainingSeconds)
        {
            if (_isDisposed)
                return UniTask.CompletedTask;

            lock (_eventLock)
            {
                _eventSequence++;
                
                _pendingEvents.Add(new OnlineFlowQueuedEvent(
                    flowEvent,
                    priority,
                    _eventSequence,
                    epoch,
                    errorCode,
                    rawSessionIdInput,
                    region,
                    isReady,
                    countdownRemainingSeconds));

                if (_isProcessing)
                    return UniTask.CompletedTask;

                _isProcessing = true;
            }

            return ProcessQueueAsync();
        }

        public IDisposable HoldForTests()
        {
            Monitor.Enter(_eventLock);

            if (_isProcessing)
            {
                Monitor.Exit(_eventLock);
                throw new InvalidOperationException("Cannot hold event queue while processing is already active.");
            }

            _isProcessing = true;
            return new TestQueueHold(this);
        }

        public UniTask DrainForTestsAsync()
        {
            if (_isDisposed)
                return UniTask.CompletedTask;

            lock (_eventLock)
            {
                if (_isProcessing || _pendingEvents.Count == 0)
                    return UniTask.CompletedTask;

                _isProcessing = true;
            }

            return ProcessQueueAsync();
        }

        public void Dispose()
        {
            lock (_eventLock)
            {
                _isDisposed = true;
                _pendingEvents.Clear();
            }
        }

        private async UniTask ProcessQueueAsync()
        {
            try
            {
                while (true)
                {
                    OnlineFlowQueuedEvent? queued;

                    lock (_eventLock)
                    {
                        if (_pendingEvents.Count == 0)
                        {
                            _isProcessing = false;
                            return;
                        }

                        var bestIndex = GetNextEventIndex();
                        queued = _pendingEvents[bestIndex];
                        _pendingEvents.RemoveAt(bestIndex);
                    }

                    await UniTask.SwitchToMainThread();

                    if (queued.HasValue)
                        _applyEvent(queued.Value);
                }
            }
            finally
            {
                lock (_eventLock)
                {
                    if (_pendingEvents.Count == 0)
                        _isProcessing = false;
                }
            }
        }

        private int GetNextEventIndex()
        {
            var bestIndex = 0;

            for (var i = 1; i < _pendingEvents.Count; i++)
            {
                if (Compare(_pendingEvents[i], _pendingEvents[bestIndex]) < 0)
                    bestIndex = i;
            }

            return bestIndex;
        }

        private static int Compare(OnlineFlowQueuedEvent left, OnlineFlowQueuedEvent right)
        {
            var priorityCmp = left.Priority.CompareTo(right.Priority);
            return priorityCmp != 0 ? priorityCmp : left.Sequence.CompareTo(right.Sequence);
        }

        private sealed class TestQueueHold : IDisposable
        {
            private readonly OnlineFlowEventQueue _owner;
            private bool _isDisposed;

            public TestQueueHold(OnlineFlowEventQueue owner) => _owner = owner;

            public void Dispose()
            {
                if (_isDisposed)
                    return;

                _isDisposed = true;
                _owner._isProcessing = false;
                Monitor.Exit(_owner._eventLock);
            }
        }
    }
}