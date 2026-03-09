#nullable enable

using System.Threading;
using Cysharp.Threading.Tasks;

namespace Runtime.GameModes.Wizard.Matchmaking
{
    /// <summary>
    /// Service responsible for finding online matches.
    /// </summary>
    public interface IMatchmakingService
    {
        UniTask<QueueEntry> EnterQueueAsync(MatchmakingRequest request, CancellationToken ct);
        UniTask<MatchmakingResult> WaitForMatchAsync(QueueEntry entry, CancellationToken ct);
        UniTask LeaveAsync(CancellationToken ct);
    }
}