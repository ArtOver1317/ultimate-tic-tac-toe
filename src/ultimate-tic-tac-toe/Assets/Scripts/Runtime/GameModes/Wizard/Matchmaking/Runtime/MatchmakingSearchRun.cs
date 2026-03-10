#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Runtime.GameModes.Wizard.Matchmaking.Runtime
{
    internal sealed class MatchmakingSearchRun : IDisposable
    {
        private CancellationTokenSource? _userCancelCts;
        private CancellationTokenSource? _timeoutCts;

        public CancellationToken UserCancelToken => _userCancelCts?.Token ?? CancellationToken.None;
        public CancellationToken TimeoutToken => _timeoutCts?.Token ?? CancellationToken.None;
        public Task? WaitTask { get; private set; }
        public int Epoch { get; private set; }

        public int Begin(TimeSpan timeout)
        {
            Clear();

            Epoch++;
            _userCancelCts = new CancellationTokenSource();
            _timeoutCts = new CancellationTokenSource(timeout);
            return Epoch;
        }

        public void SetWaitTask(Task waitTask) => WaitTask = waitTask ?? throw new ArgumentNullException(nameof(waitTask));

        public void RequestUserCancel() => _userCancelCts?.Cancel();

        public void CancelAll()
        {
            _userCancelCts?.Cancel();
            _timeoutCts?.Cancel();
        }

        public void Clear()
        {
            _userCancelCts?.Dispose();
            _userCancelCts = null;

            _timeoutCts?.Dispose();
            _timeoutCts = null;

            WaitTask = null;
        }

        public void Dispose()
        {
            CancelAll();
            Clear();
        }
    }
}