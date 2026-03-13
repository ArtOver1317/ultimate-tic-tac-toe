#nullable enable

using Cysharp.Threading.Tasks;
using R3;

namespace Runtime.Games.Battleship.Networking
{
    public sealed class NoOpBattleshipNetworkBridge : IBattleshipNetworkBridge
    {
        private readonly Subject<BattleshipPlacementMessage> _incomingPlacements = new();
        private readonly Subject<BattleshipPlacementTimeoutMessage> _incomingPlacementTimeouts = new();
        private readonly Subject<BattleshipRecoveryMessage> _incomingRecoverySnapshots = new();

        public static readonly NoOpBattleshipNetworkBridge Instance = new();

        public Observable<BattleshipPlacementMessage> IncomingPlacements => _incomingPlacements;

        public Observable<BattleshipPlacementTimeoutMessage> IncomingPlacementTimeouts => _incomingPlacementTimeouts;

        public Observable<BattleshipRecoveryMessage> IncomingRecoverySnapshots => _incomingRecoverySnapshots;

        public UniTask BindAsync(string localUserId, bool isHost) => UniTask.CompletedTask;

        public UniTask UnbindAsync() => UniTask.CompletedTask;

        public UniTask SubmitPlacementAsync(BattleshipPlacementMessage message) => UniTask.CompletedTask;

        public UniTask SubmitPlacementTimeoutAsync(BattleshipPlacementTimeoutMessage message) => UniTask.CompletedTask;

        public UniTask SubmitRecoverySnapshotAsync(BattleshipRecoveryMessage message) => UniTask.CompletedTask;

        public void Dispose()
        {
            _incomingPlacements.Dispose();
            _incomingPlacementTimeouts.Dispose();
            _incomingRecoverySnapshots.Dispose();
        }
    }
}