#nullable enable

using System.Threading;
using Cysharp.Threading.Tasks;

namespace Runtime.GameModes.Wizard
{
    /// <summary>
    /// Service responsible for finding online matches.
    /// </summary>
    public interface IMatchmakingService
    {
        /// <summary>
        /// Finds a match for the provided request.
        /// </summary>
        UniTask<MatchmakingResult> FindMatchAsync(MatchmakingRequest request, CancellationToken ct);
    }
}