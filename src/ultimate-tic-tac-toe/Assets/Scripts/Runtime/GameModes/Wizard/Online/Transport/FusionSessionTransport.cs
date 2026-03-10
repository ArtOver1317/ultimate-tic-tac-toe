#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Fusion;
using Fusion.Photon.Realtime;
using Fusion.Sockets;
using Runtime.GameModes.Wizard.Matchmaking.Contracts;
using UnityEngine;

namespace Runtime.GameModes.Wizard.Online
{
    public sealed class FusionSessionTransport : MonoBehaviour, IPhotonSessionTransport, INetworkRunnerCallbacks
    {
        private const string _runnerObjectName = "OnlineFusionRunner";
        private static readonly TimeSpan _remoteRecipientReadyTimeout = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan _remoteRecipientPollDelay = TimeSpan.FromMilliseconds(20);
        
        private static readonly ReliableKey _gameplayReliableKey = ReliableKey.FromInts(
            0x55545454,
            0x4f4e4c59,
            0x4d4f5645,
            1);

        private NetworkRunner? _runner;
        private bool _isDisposed;
        private double _lastKnownNetworkTimeSeconds;
        private string? _lastSessionName;
        private GameMode? _lastGameMode;

        public event Action<PhotonTransportLifecycleEvent>? LifecycleEvent;
        public event Action<PhotonReliableDataEvent>? ReliableDataReceived;
        public bool IsInSession => _runner != null && _runner.IsRunning;
        public bool IsServerRole => _runner != null && _runner.IsServer;

        public double NetworkTimeSeconds
        {
            get
            {
                if (!FusionRunnerHelpers.TryResolveNetworkTime(_runner, out var networkTime))
                    return ResolveFallbackTime();

                _lastKnownNetworkTimeSeconds = networkTime;
                return networkTime;
            }
        }

        public async UniTask CreateHostSessionAsync(OnlineSessionConfig config)
        {
            EnsureNotDisposed();

            var sessionName = config.SessionId.Value;
            
            await StartSessionAsync(
                config.Region,
                new StartGameArgs
                {
                    GameMode = GameMode.Host,
                    SessionName = sessionName,
                },
                "Create host");

            RememberSessionBinding(sessionName, GameMode.Host);
            RaiseCurrentSessionLifecycle("host_created", config.HostUserId);
        }

        public async UniTask JoinSessionAsync(SessionId sessionId, string region, string currentUserId)
        {
            EnsureNotDisposed();

            if (string.IsNullOrWhiteSpace(region))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(region));

            if (string.IsNullOrWhiteSpace(currentUserId))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(currentUserId));

            var sessionName = sessionId.Value;
            
            await StartSessionAsync(
                region,
                new StartGameArgs
                {
                    GameMode = GameMode.Client,
                    SessionName = sessionName,
                },
                "Join");

            RememberSessionBinding(sessionName, GameMode.Client);
            RaiseCurrentSessionLifecycle("join_succeeded", currentUserId);
        }

        public async UniTask<PhotonTransportMatchmakingResult> JoinRandomOrCreateSessionAsync(MatchmakingRoomOptions options, CancellationToken ct)
        {
            EnsureNotDisposed();

            if (options == null)
                throw new ArgumentNullException(nameof(options));

            ct.ThrowIfCancellationRequested();

            var runner = await StartSessionAsync(
                options.Region,
                new StartGameArgs
                {
                    GameMode = GameMode.AutoHostOrClient,
                    EnableClientSessionCreation = true,
                    PlayerCount = options.MaxPlayers,
                    SessionProperties = BuildSessionProperties(options),
                },
                "JoinRandomOrCreate",
                ct);

            return CreateMatchmakingResult(runner);
        }

        public async UniTask LeaveSessionAsync()
        {
            if (_isDisposed)
                return;

            await UniTask.SwitchToMainThread();

            var sessionName = _lastSessionName;
            await DisposeRunnerAsync();
            RaiseLifecycle(OnlineGatewayEventKinds.LeftRoom, sessionName, null);
        }

        public async UniTask ReconnectAsync(string region, string currentUserId)
        {
            EnsureNotDisposed();

            if (string.IsNullOrWhiteSpace(region))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(region));

            if (string.IsNullOrWhiteSpace(currentUserId))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(currentUserId));

            if (string.IsNullOrWhiteSpace(_lastSessionName) || !_lastGameMode.HasValue)
                throw new InvalidOperationException("Reconnect requires previous session binding.");

            var runner = await StartSessionAsync(
                region,
                new StartGameArgs
                {
                    GameMode = _lastGameMode.Value,
                    SessionName = _lastSessionName,
                },
                "Reconnect");

            if (!FusionRunnerHelpers.IsConnectedToServer(runner))
                throw new PhotonSessionTransportException(OnlineErrorCode.NetworkUnavailable, "Reconnect attempt did not restore server connection.");

            RaiseCurrentSessionLifecycle("reconnect_succeeded", currentUserId);
        }

        private double ResolveFallbackTime() => 
            _lastKnownNetworkTimeSeconds > 0d 
                ? _lastKnownNetworkTimeSeconds 
                : Time.realtimeSinceStartupAsDouble;

        private async UniTask<NetworkRunner> StartSessionAsync(
            string region,
            StartGameArgs args,
            string operationName,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            if (ct.CanBeCanceled)
                await UniTask.SwitchToMainThread(ct);
            else
                await UniTask.SwitchToMainThread();

            TryApplyFixedRegion(region);

            var runner = await EnsureRunnerReadyAsync();
            var result = await runner.StartGame(args);
            EnsureStartGameSucceeded(result, operationName);
            return runner;
        }

        public async UniTask SendReliableDataAsync(byte[] payload)
        {
            EnsureNotDisposed();

            if (payload == null)
                throw new ArgumentNullException(nameof(payload));

            if (payload.Length == 0)
                return;

            await UniTask.SwitchToMainThread();

            if (_runner == null || !_runner.IsRunning)
                throw new InvalidOperationException("Reliable data send requires active runner.");

            if (_runner.IsServer)
            {
                var hasRemoteRecipient = await FusionRunnerHelpers.WaitForRemoteRecipientAsync(
                    _runner,
                    _remoteRecipientReadyTimeout,
                    _remoteRecipientPollDelay);
                
                if (!hasRemoteRecipient)
                    throw new InvalidOperationException("Reliable data send requires at least one remote player.");

                foreach (var player in _runner.ActivePlayers)
                {
                    if (player == _runner.LocalPlayer)
                        continue;

                    _runner.SendReliableDataToPlayer(player, _gameplayReliableKey, payload);
                }

                return;
            }

            _runner.SendReliableDataToServer(_gameplayReliableKey, payload);
        }

        private async UniTask<NetworkRunner> EnsureRunnerReadyAsync()
        {
            if (_runner != null) 
                await DisposeRunnerAsync();

            var go = new GameObject(_runnerObjectName);
            DontDestroyOnLoad(go);
            _runner = go.AddComponent<NetworkRunner>();
            _runner.ProvideInput = false;
            _runner.AddCallbacks(this);
            return _runner;
        }

        private static Dictionary<string, SessionProperty> BuildSessionProperties(MatchmakingRoomOptions options) =>
            new()
            {
                ["gameId"] = options.GameId,
                ["ph"] = options.ParamsHash,
            };

        private PhotonTransportMatchmakingResult CreateMatchmakingResult(NetworkRunner runner)
        {
            var sessionName = runner.SessionInfo.Name;

            if (string.IsNullOrWhiteSpace(sessionName))
            {
                throw new PhotonSessionTransportException(
                    OnlineErrorCode.NetworkUnavailable,
                    "JoinRandomOrCreate succeeded but session name is empty.");
            }

            var gameMode = runner.IsServer ? GameMode.Host : GameMode.Client;
            var remotePlayer = FusionRunnerHelpers.GetRemotePlayerId(runner);
            var playerCount = FusionRunnerHelpers.CountPlayers(runner);

            RememberSessionBinding(sessionName, gameMode);
            RaiseCurrentSessionLifecycle("matchmaking_entered", remotePlayer);
            return new PhotonTransportMatchmakingResult(sessionName, playerCount, remotePlayer, runner.IsServer);
        }

        private static void TryApplyFixedRegion(string region)
        {
            if (string.IsNullOrWhiteSpace(region))
                return;

            try
            {
                var global = PhotonAppSettings.Global;
                
                if (global?.AppSettings == null)
                    return;

                global.AppSettings.FixedRegion = region;
            }
            catch
            {
                // ignored
            }
        }

        private async UniTask DisposeRunnerAsync()
        {
            var runner = _runner;
            _runner = null;

            if (runner == null)
                return;

            if (runner.IsRunning)
            {
                await runner.Shutdown();
                await UniTask.Yield();
            }

            ReleaseRunner(runner);
        }

        private void RaiseLifecycle(string kind, string? sessionId, string? userId) =>
            LifecycleEvent?.Invoke(new PhotonTransportLifecycleEvent(kind, sessionId, userId));

        private void RaiseCurrentSessionLifecycle(string kind, string? userId) =>
            RaiseLifecycle(kind, _lastSessionName, userId);

        private void RememberSessionBinding(string sessionName, GameMode gameMode)
        {
            _lastSessionName = sessionName;
            _lastGameMode = gameMode;
        }

        private static void EnsureStartGameSucceeded(StartGameResult result, string operationName)
        {
            if (result.Ok)
                return;

            throw new PhotonSessionTransportException(
                MapShutdownReason(result.ShutdownReason.ToString()),
                $"{operationName} failed: {result.ShutdownReason}");
        }

        private static OnlineErrorCode MapShutdownReason(string shutdownReason)
        {
            if (string.IsNullOrWhiteSpace(shutdownReason))
                return OnlineErrorCode.NetworkUnavailable;

            if (ContainsAny(shutdownReason, "NotFound", "GameNotFound", "SessionNotFound", "NoSession"))
                return OnlineErrorCode.SessionNotFound;

            if (ContainsAny(shutdownReason, "GameClosed", "GameFull", "MaxPlayers", "SessionFull"))
                return OnlineErrorCode.SessionFull;

            if (ContainsAny(shutdownReason, "IncompatibleRegion", "Region", "Lobby"))
                return OnlineErrorCode.RegionMismatchOrUnavailable;

            return ContainsAny(shutdownReason, "AlreadyInGame", "InProgress") 
                ? OnlineErrorCode.SessionAlreadyInGame 
                : OnlineErrorCode.NetworkUnavailable;
        }

        private static bool ContainsAny(string source, params string[] values)
        {
            foreach (var value in values)
            {
                if (source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        private void EnsureNotDisposed()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(FusionSessionTransport));
        }

        private void ReleaseRunner(NetworkRunner runner)
        {
            runner.RemoveCallbacks(this);

            var runnerGameObject = runner.gameObject;

            if (runnerGameObject != null)
                Destroy(runnerGameObject);
        }

        private void OnDestroy()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;

            var runner = _runner;
            _runner = null;

            if (runner == null)
                return;

            if (runner != null && runner.IsRunning)
                runner.Shutdown();

            if (runner != null) 
                ReleaseRunner(runner);
        }

        void INetworkRunnerCallbacks.OnPlayerJoined(NetworkRunner runner, PlayerRef player)
        {
            if (!runner.IsRunning)
                return;

            if (player == runner.LocalPlayer)
                return;

            RaiseCurrentSessionLifecycle(OnlineGatewayEventKinds.PeerJoined, player.ToString());
        }

        void INetworkRunnerCallbacks.OnPlayerLeft(NetworkRunner runner, PlayerRef player) =>
            RaiseCurrentSessionLifecycle(OnlineGatewayEventKinds.PeerLeft, player.ToString());

        void INetworkRunnerCallbacks.OnConnectedToServer(NetworkRunner runner) =>
            RaiseCurrentSessionLifecycle("connected", null);

        void INetworkRunnerCallbacks.OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) =>
            RaiseCurrentSessionLifecycle(OnlineGatewayEventKinds.Disconnected, reason.ToString());

        void INetworkRunnerCallbacks.OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason) =>
            RaiseCurrentSessionLifecycle(OnlineGatewayEventKinds.Shutdown, shutdownReason.ToString());

        void INetworkRunnerCallbacks.OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) =>
            RaiseCurrentSessionLifecycle(OnlineGatewayEventKinds.ConnectFailed, reason.ToString());

        void INetworkRunnerCallbacks.OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
        void INetworkRunnerCallbacks.OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
        void INetworkRunnerCallbacks.OnInput(NetworkRunner runner, NetworkInput input) { }
        void INetworkRunnerCallbacks.OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
        void INetworkRunnerCallbacks.OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
        void INetworkRunnerCallbacks.OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
        void INetworkRunnerCallbacks.OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
        void INetworkRunnerCallbacks.OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
        void INetworkRunnerCallbacks.OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
        
        void INetworkRunnerCallbacks.OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data)
        {
            if (key != _gameplayReliableKey)
                return;

            if (data.Array == null || data.Count <= 0)
                return;

            var payload = new byte[data.Count];
            Array.Copy(data.Array, data.Offset, payload, 0, data.Count);
            ReliableDataReceived?.Invoke(new PhotonReliableDataEvent(payload));
        }
        
        void INetworkRunnerCallbacks.OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
        void INetworkRunnerCallbacks.OnSceneLoadStart(NetworkRunner runner) { }
        void INetworkRunnerCallbacks.OnSceneLoadDone(NetworkRunner runner) { }
    }
}