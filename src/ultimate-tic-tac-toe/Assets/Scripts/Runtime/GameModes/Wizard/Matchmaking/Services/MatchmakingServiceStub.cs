#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Runtime.GameModes.Wizard.Matchmaking.Contracts;

namespace Runtime.GameModes.Wizard.Matchmaking.Services
{
    /// <summary>
    /// Temporary stub matchmaking service.
    /// </summary>
    public sealed class MatchmakingServiceStub : IMatchmakingService
    {
        public UniTask<QueueEntry> EnterQueueAsync(MatchmakingRequest request, CancellationToken ct)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            ct.ThrowIfCancellationRequested();

            return UniTask.FromException<QueueEntry>(
                new NotSupportedException("Matchmaking service is not implemented yet."));
        }

        public UniTask<MatchmakingResult> WaitForMatchAsync(QueueEntry entry, CancellationToken ct)
        {
            if (entry == null)
                throw new ArgumentNullException(nameof(entry));

            ct.ThrowIfCancellationRequested();

            return UniTask.FromException<MatchmakingResult>(
                new NotSupportedException("Matchmaking service is not implemented yet."));
        }

        public UniTask LeaveAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return UniTask.CompletedTask;
        }
    }
}