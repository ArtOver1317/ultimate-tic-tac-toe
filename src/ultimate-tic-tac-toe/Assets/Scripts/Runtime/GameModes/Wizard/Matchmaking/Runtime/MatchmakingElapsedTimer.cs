#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.Infrastructure.Logging;

namespace Runtime.GameModes.Wizard.Matchmaking.Runtime
{
    internal sealed class MatchmakingElapsedTimer : IDisposable
    {
        private readonly ReactiveProperty<TimeSpan> _elapsedTime = new(TimeSpan.Zero);

        private CancellationTokenSource? _timerCts;
        private DateTime _searchStartUtc;

        public ReadOnlyReactiveProperty<TimeSpan> ElapsedTime => _elapsedTime;

        public void Start()
        {
            if (_timerCts != null)
                return;

            _searchStartUtc = DateTime.UtcNow;

            var cts = new CancellationTokenSource();
            _timerCts = cts;

            RunAsync(cts.Token).Forget();
        }

        public void Stop(bool resetElapsed)
        {
            if (resetElapsed)
                _elapsedTime.Value = TimeSpan.Zero;

            var cts = Interlocked.Exchange(ref _timerCts, null);
            
            if (cts == null)
                return;

            try
            {
                cts.Cancel();
            }
            finally
            {
                cts.Dispose();
            }
        }

        public void Dispose()
        {
            Stop(resetElapsed: false);
            _elapsedTime.Dispose();
        }

        private async UniTaskVoid RunAsync(CancellationToken ct)
        {
            try
            {
                await UniTask.SwitchToMainThread(ct);

                while (!ct.IsCancellationRequested)
                {
                    _elapsedTime.Value = DateTime.UtcNow - _searchStartUtc;
                    await UniTask.Delay(TimeSpan.FromSeconds(1), cancellationToken: ct);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                GameLog.Exception(ex);
            }
        }
    }
}