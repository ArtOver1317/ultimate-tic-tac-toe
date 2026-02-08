#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Runtime.GameModes.Wizard
{
    /// <summary>
    /// Temporary stub matchmaking service.
    /// </summary>
    public sealed class MatchmakingServiceStub : IMatchmakingService
    {
        public UniTask<MatchmakingResult> FindMatchAsync(MatchmakingRequest request, CancellationToken ct)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            ct.ThrowIfCancellationRequested();

            return UniTask.FromException<MatchmakingResult>(
                new NotSupportedException("Matchmaking service is not implemented yet."));
        }
    }
}