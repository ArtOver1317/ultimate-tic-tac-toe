#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using Cysharp.Threading.Tasks;
using NSubstitute;
using R3;
using Runtime.GameModes.Wizard.Online;
using Runtime.Games.Battleship.Networking;

namespace Tests.EditMode.Games.Battleship.Fakes
{
    internal static class BattleshipNetworkingTestPayload
    {
        public static string Encode(string payload) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));

        public static void RaiseReliableData(IPhotonSessionTransport transport, string text)
        {
            var payload = new PhotonReliableDataEvent(Encoding.UTF8.GetBytes(text));
            transport.ReliableDataReceived += Raise.Event<Action<PhotonReliableDataEvent>>(payload);
        }
    }

    internal sealed class SpyGameplayNetworkBridge : IGameplayNetworkBridge
    {
        private readonly ReactiveProperty<GameplayNetworkSnapshot?> _snapshot = new(null);
        private readonly Subject<MoveCommand> _incomingMoves = new();
        private readonly Subject<RoundReadySignal> _incomingRoundReadySignals = new();
        private readonly Subject<OnlineTimeoutSignal> _incomingTimeoutSignals = new();

        public List<MoveCommand> SubmittedMoves { get; } = new();

        public ReadOnlyReactiveProperty<GameplayNetworkSnapshot?> Snapshot => _snapshot;
        public Observable<MoveCommand> IncomingMoves => _incomingMoves;
        public Observable<RoundReadySignal> IncomingRoundReadySignals => _incomingRoundReadySignals;
        public Observable<OnlineTimeoutSignal> IncomingTimeoutSignals => _incomingTimeoutSignals;

        public UniTask BindAsync(string localUserId, bool isHost) => UniTask.CompletedTask;

        public UniTask UnbindAsync() => UniTask.CompletedTask;

        public UniTask SubmitMoveAsync(MoveCommand command)
        {
            SubmittedMoves.Add(command);
           
            _snapshot.Value = new GameplayNetworkSnapshot(
                matchRoundId: 1,
                isCompleted: false,
                winnerUserId: null,
                authoritativeTick: SubmittedMoves.Count,
                countdownTargetTick: command.ClientTick,
                shotSequence: command.ClientTick);
          
            return UniTask.CompletedTask;
        }

        public UniTask SubmitRoundReadyAsync(RoundReadySignal signal) => UniTask.CompletedTask;

        public UniTask SubmitTimeoutAsync(OnlineTimeoutSignal signal) => UniTask.CompletedTask;

        public void SetShotSequence(long sequence) =>
            _snapshot.Value = new GameplayNetworkSnapshot(
                matchRoundId: 1,
                isCompleted: false,
                winnerUserId: null,
                authoritativeTick: 0,
                countdownTargetTick: 0,
                shotSequence: sequence);

        public void Dispose()
        {
            _snapshot.Dispose();
            _incomingMoves.Dispose();
            _incomingRoundReadySignals.Dispose();
            _incomingTimeoutSignals.Dispose();
        }
    }

    internal sealed class SpyBattleshipNetworkBridge : IBattleshipNetworkBridge
    {
        public Subject<BattleshipPlacementMessage> PlacementSubject { get; } = new();
        public Subject<BattleshipPlacementTimeoutMessage> TimeoutSubject { get; } = new();
        public Subject<BattleshipRecoveryMessage> RecoverySubject { get; } = new();

        public Observable<BattleshipPlacementMessage> IncomingPlacements => PlacementSubject;
        public Observable<BattleshipPlacementTimeoutMessage> IncomingPlacementTimeouts => TimeoutSubject;
        public Observable<BattleshipRecoveryMessage> IncomingRecoverySnapshots => RecoverySubject;

        public UniTask BindAsync(string localUserId, bool isHost) => UniTask.CompletedTask;

        public UniTask UnbindAsync() => UniTask.CompletedTask;

        public UniTask SubmitPlacementAsync(BattleshipPlacementMessage message) => UniTask.CompletedTask;

        public UniTask SubmitPlacementTimeoutAsync(BattleshipPlacementTimeoutMessage message) => UniTask.CompletedTask;

        public UniTask SubmitRecoverySnapshotAsync(BattleshipRecoveryMessage message) => UniTask.CompletedTask;

        public void Dispose()
        {
            PlacementSubject.Dispose();
            TimeoutSubject.Dispose();
            RecoverySubject.Dispose();
        }
    }
}