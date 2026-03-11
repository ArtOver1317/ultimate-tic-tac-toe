#nullable enable

using System.Threading;
using Cysharp.Threading.Tasks;

namespace Runtime.GameModes.Wizard.Coordinator
{
    internal sealed class WizardIntentQueue
    {
        private readonly object _lock = new();
        private WizardIntent? _pendingIntent;
        private UniTaskCompletionSource<bool>? _signal;

        public bool TryEnqueue(WizardIntent intent)
        {
            lock (_lock)
            {
                // Anti-spam policy: we only allow a single pending non-cancel intent.
                // This keeps memory bounded while guaranteeing that accepted intents are not silently dropped.
                if (_pendingIntent.HasValue)
                    return false;

                _pendingIntent = intent;
                // Signal waiter but do NOT clear _signal here - consumer owns clearing it.
                _signal?.TrySetResult(true);
                return true;
            }
        }

        public async UniTask<WizardIntent> DequeueAsync(CancellationToken ct)
        {
            while (true)
            {
                UniTask waitTask;

                lock (_lock)
                {
                    if (_pendingIntent.HasValue)
                    {
                        var intent = _pendingIntent.Value;
                        _pendingIntent = null;
                        return intent;
                    }

                    _signal ??= new UniTaskCompletionSource<bool>();
                    waitTask = _signal.Task;
                }

                await waitTask.AttachExternalCancellation(ct);

                // Consumer owns clearing the signal after awaiting it.
                // This prevents race where TryEnqueue clears signal before we consume the item.
                lock (_lock)
                {
                    _signal = null;
                }
            }
        }
    }
}