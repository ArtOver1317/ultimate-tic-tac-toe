#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using R3;

namespace Runtime.GameModes.Wizard.Online
{
    internal sealed class PhotonGatewayLifecycleTracker : IDisposable
    {
        private const int _historyCapacity = 128;

        private readonly ReactiveProperty<GatewayLifecycleEvent?> _lifecycleEvent = new(null);
        private readonly object _historyLock = new();
        private readonly Queue<GatewayLifecycleEvent> _history = new();

        private int _sequence;

        public ReadOnlyReactiveProperty<GatewayLifecycleEvent?> LifecycleEvent => _lifecycleEvent;

        public void Publish(PhotonTransportLifecycleEvent evt)
        {
            var sequence = Interlocked.Increment(ref _sequence);
            var mapped = new GatewayLifecycleEvent(evt.Kind, evt.SessionId, evt.UserId, sequence);

            lock (_historyLock)
            {
                _history.Enqueue(mapped);

                while (_history.Count > _historyCapacity)
                    _history.Dequeue();
            }

            _lifecycleEvent.Value = mapped;
        }

        public GatewayLifecycleEvent[] GetEventsSince(int sequenceExclusive)
        {
            lock (_historyLock)
            {
                if (_history.Count == 0)
                    return Array.Empty<GatewayLifecycleEvent>();

                var result = new List<GatewayLifecycleEvent>(_history.Count);

                foreach (var evt in _history)
                {
                    if (evt.Sequence > sequenceExclusive)
                        result.Add(evt);
                }

                return result.ToArray();
            }
        }

        public void Dispose()
        {
            lock (_historyLock)
            {
                _history.Clear();
            }

            _lifecycleEvent.Dispose();
        }
    }
}