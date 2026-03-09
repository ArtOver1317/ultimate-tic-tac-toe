#nullable enable

using System;
using System.Collections.Generic;

namespace Runtime.GameModes.Wizard.Online
{
    public readonly struct OnlineDiagnosticEvent
    {
        public DateTimeOffset TimestampUtc { get; }
        public string EventName { get; }
        public string? SessionId { get; }
        public string Region { get; }
        public string LocalUserId { get; }
        public OnlineFlowState FlowState { get; }
        public int FlowEpoch { get; }
        public long EventSequence { get; }
        public Guid CorrelationId { get; }
        public string? Reason { get; }
        public OnlineErrorCode ErrorCode { get; }

        public OnlineDiagnosticEvent(
            DateTimeOffset timestampUtc,
            string eventName,
            string? sessionId,
            string region,
            string localUserId,
            OnlineFlowState flowState,
            int flowEpoch,
            long eventSequence,
            Guid correlationId,
            string? reason,
            OnlineErrorCode errorCode)
        {
            if (string.IsNullOrWhiteSpace(eventName))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(eventName));

            if (string.IsNullOrWhiteSpace(region))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(region));

            if (string.IsNullOrWhiteSpace(localUserId))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(localUserId));

            if (flowEpoch < 1)
                throw new ArgumentOutOfRangeException(nameof(flowEpoch), flowEpoch, "Value must be at least 1.");

            if (eventSequence < 0)
                throw new ArgumentOutOfRangeException(nameof(eventSequence), eventSequence, "Value cannot be negative.");

            TimestampUtc = timestampUtc;
            EventName = eventName;
            SessionId = sessionId;
            Region = region;
            LocalUserId = localUserId;
            FlowState = flowState;
            FlowEpoch = flowEpoch;
            EventSequence = eventSequence;
            CorrelationId = correlationId == Guid.Empty ? Guid.NewGuid() : correlationId;
            Reason = reason;
            ErrorCode = errorCode;
        }
    }

    public sealed class OnlineDiagnosticsBuffer
    {
        private readonly int _capacity;
        private readonly Queue<OnlineDiagnosticEvent> _buffer;

        public OnlineDiagnosticsBuffer(int capacity = 500)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Value must be positive.");

            _capacity = capacity;
            _buffer = new Queue<OnlineDiagnosticEvent>(capacity);
        }

        public int Count => _buffer.Count;

        public void Track(OnlineDiagnosticEvent evt)
        {
            while (_buffer.Count >= _capacity)
                _buffer.Dequeue();

            _buffer.Enqueue(evt);
        }

        public IReadOnlyList<OnlineDiagnosticEvent> Flush()
        {
            var events = _buffer.ToArray();
            _buffer.Clear();
            return events;
        }
    }

    public sealed class OnlineCleanupTracker
    {
        public int ActiveRunnerCount { get; private set; }
        public int ActiveReconnectTimers { get; private set; }
        public int SessionSubscriptions { get; private set; }

        public void OnRunnerAllocated() => ActiveRunnerCount++;
        public void OnRunnerReleased() => ActiveRunnerCount = Math.Max(0, ActiveRunnerCount - 1);

        public void OnReconnectTimerStarted() => ActiveReconnectTimers++;
        public void OnReconnectTimerStopped() => ActiveReconnectTimers = Math.Max(0, ActiveReconnectTimers - 1);

        public void OnSessionSubscribed() => SessionSubscriptions++;
        public void OnSessionUnsubscribed() => SessionSubscriptions = Math.Max(0, SessionSubscriptions - 1);

        public void ResetAll()
        {
            ActiveRunnerCount = 0;
            ActiveReconnectTimers = 0;
            SessionSubscriptions = 0;
        }

        public bool IsCleanupSatisfied() =>
            ActiveRunnerCount == 0 &&
            ActiveReconnectTimers == 0 &&
            SessionSubscriptions == 0;
    }
}

#nullable restore