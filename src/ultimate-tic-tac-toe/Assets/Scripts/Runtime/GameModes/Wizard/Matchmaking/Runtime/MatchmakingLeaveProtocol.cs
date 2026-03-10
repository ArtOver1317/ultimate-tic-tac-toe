#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Runtime.GameModes.Wizard.Matchmaking.Contracts;
using Runtime.Infrastructure.Logging;

namespace Runtime.GameModes.Wizard.Matchmaking.Runtime
{
    internal static class MatchmakingLeaveProtocol
    {
        public static async UniTask<CancelAckOutcome> ExecuteCancelAckAsync(
            IMatchmakingService service,
            TimeSpan cancelAckTimeout)
        {
            using var ackCts = new CancellationTokenSource(cancelAckTimeout);

            try
            {
                await service.LeaveAsync(ackCts.Token);
                return CancelAckOutcome.Success;
            }
            catch (MatchmakingCancelAckTimeoutException)
            {
                return CancelAckOutcome.Timeout;
            }
            catch (ConnectionLostException)
            {
                return CancelAckOutcome.ConnectionLost;
            }
            catch (OperationCanceledException)
            {
                return ackCts.IsCancellationRequested
                    ? CancelAckOutcome.Timeout
                    : CancelAckOutcome.ConnectionLost;
            }
            catch (Exception ex)
            {
                GameLog.Exception(ex);
                return CancelAckOutcome.ConnectionLost;
            }
        }

        public static async UniTaskVoid RequestBestEffortLeaveAsync(
            IMatchmakingService service,
            TimeSpan cancelAckTimeout)
        {
            using var leaveCts = new CancellationTokenSource(cancelAckTimeout);

            try
            {
                await service.LeaveAsync(leaveCts.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                GameLog.Exception(ex);
            }
        }
    }

    internal enum CancelAckOutcome
    {
        Success = 0,
        Timeout = 1,
        ConnectionLost = 2,
    }
}