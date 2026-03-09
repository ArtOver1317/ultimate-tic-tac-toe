#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.GameModes.Wizard.Online;

namespace Runtime.GameModes.Wizard.Matchmaking
{
    public sealed class PhotonMatchmakingService : IMatchmakingService, IDisposable
    {
        private readonly PhotonSessionGateway _gateway;

        private bool _disposed;
        private int _outcome;
        private int _eventSequenceFence;

        public PhotonMatchmakingService(PhotonSessionGateway gateway) =>
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));

        public async UniTask<QueueEntry> EnterQueueAsync(MatchmakingRequest request, CancellationToken ct)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            ct.ThrowIfCancellationRequested();

            EnsureNotDisposed();

            var roomOptions = new MatchmakingRoomOptions(
                region: OnlineIdentityProvider.ResolveDefaultRegion(),
                gameId: MatchmakingParamsHasher.NormalizeGameId(request.GameId),
                paramsHash: MatchmakingParamsHasher.Compute(request),
                maxPlayers: 2);

            _eventSequenceFence = _gateway.LifecycleEvent.CurrentValue?.Sequence ?? 0;
            var room = await _gateway.JoinRandomOrCreateAsync(roomOptions, ct);

            if (room.PlayersCount >= 2 && !string.IsNullOrWhiteSpace(room.OpponentId))
            {
                var immediate = new MatchmakingResult(room.RoomName, room.OpponentId, room.IsHost);
                return new QueueEntry(room.RoomName, immediate);
            }

            return new QueueEntry(room.RoomName, immediateResult: null);
        }

        public async UniTask<MatchmakingResult> WaitForMatchAsync(QueueEntry entry, CancellationToken ct)
        {
            if (entry == null)
                throw new ArgumentNullException(nameof(entry));

            EnsureNotDisposed();

            if (entry.IsPaired && entry.ImmediateResult != null)
                return entry.ImmediateResult;

            _outcome = 0;
            var sequenceCursor = _eventSequenceFence;
            var bufferedDuringInit = new List<GatewayLifecycleEvent>(4);
            var isInitializing = true;

            var tcs = new UniTaskCompletionSource<MatchmakingResult>();

            void ProcessEvent(GatewayLifecycleEvent data)
            {
                if (data.Sequence <= sequenceCursor)
                    return;

                sequenceCursor = data.Sequence;

                if (!IsEventForRoom(data, entry.RoomName))
                    return;

                if (string.Equals(data.Kind, "peer_joined", StringComparison.OrdinalIgnoreCase))
                {
                    if (Interlocked.CompareExchange(ref _outcome, 1, 0) != 0)
                        return;

                    var opponentId = string.IsNullOrWhiteSpace(data.UserId) ? "opponent" : data.UserId;
                    var result = new MatchmakingResult(entry.RoomName, opponentId, _gateway.IsLocalHost);
                    tcs.TrySetResult(result);
                    return;
                }

                if (IsTerminalDisconnectKind(data.Kind))
                {
                    if (Interlocked.CompareExchange(ref _outcome, 2, 0) != 0)
                        return;

                    tcs.TrySetException(new ConnectionLostException("Connection was lost while waiting for match."));
                }
            }

            using var lifecycleSubscription = _gateway.LifecycleEvent.Subscribe(evt =>
            {
                if (!evt.HasValue)
                    return;

                if (isInitializing)
                {
                    bufferedDuringInit.Add(evt.Value);
                    return;
                }

                ProcessEvent(evt.Value);
            });

            var backlogBefore = _gateway.GetLifecycleEventsSince(sequenceCursor);
            for (var i = 0; i < backlogBefore.Length; i++)
                ProcessEvent(backlogBefore[i]);

            isInitializing = false;

            if (bufferedDuringInit.Count > 1)
                bufferedDuringInit.Sort((left, right) => left.Sequence.CompareTo(right.Sequence));

            for (var i = 0; i < bufferedDuringInit.Count; i++)
                ProcessEvent(bufferedDuringInit[i]);

            var backlogAfter = _gateway.GetLifecycleEventsSince(sequenceCursor);
            for (var i = 0; i < backlogAfter.Length; i++)
                ProcessEvent(backlogAfter[i]);

            using var ctRegistration = ct.Register(() =>
            {
                if (Interlocked.CompareExchange(ref _outcome, 3, 0) != 0)
                    return;

                tcs.TrySetCanceled(ct);
            });

            try
            {
                return await tcs.Task;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
        }

        public UniTask LeaveAsync(CancellationToken ct)
        {
            EnsureNotDisposed();
            return _gateway.LeaveAsync(ct);
        }

        public void Dispose() => _disposed = true;

        private void EnsureNotDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(PhotonMatchmakingService));
        }

        private static bool IsTerminalDisconnectKind(string? kind)
        {
            if (string.IsNullOrWhiteSpace(kind))
                return false;

            return string.Equals(kind, "disconnected", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(kind, "shutdown", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(kind, "connect_failed", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsEventForRoom(GatewayLifecycleEvent data, string roomName)
        {
            if (IsTerminalDisconnectKind(data.Kind) && string.IsNullOrWhiteSpace(data.SessionId))
                return true;

            if (string.IsNullOrWhiteSpace(data.SessionId))
                return false;

            return string.Equals(data.SessionId, roomName, StringComparison.OrdinalIgnoreCase);
        }
    }
}
