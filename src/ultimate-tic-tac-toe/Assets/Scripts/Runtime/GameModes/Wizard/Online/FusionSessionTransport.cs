#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Fusion;
using Fusion.Photon.Realtime;
using Fusion.Sockets;
using Runtime.GameModes.Wizard.Matchmaking;
using UnityEngine;

namespace Runtime.GameModes.Wizard.Online
{
    public sealed class FusionSessionTransport : MonoBehaviour, IPhotonSessionTransport, INetworkRunnerCallbacks
    {
        private const string RunnerObjectName = "OnlineFusionRunner";
        private static readonly TimeSpan RemoteRecipientReadyTimeout = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan RemoteRecipientPollDelay = TimeSpan.FromMilliseconds(20);
        private static readonly ReliableKey GameplayReliableKey = ReliableKey.FromInts(
            unchecked((int)0x55545454),
            unchecked((int)0x4f4e4c59),
            unchecked((int)0x4d4f5645),
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
                if (_runner == null || !_runner.IsRunning)
                    return ResolveFallbackTime();

                try
                {
                    var runnerType = _runner.GetType();
                    var timeProperty = runnerType.GetProperty("SimulationTime") ??
                                       runnerType.GetProperty("NetworkTime") ??
                                       runnerType.GetProperty("LocalRenderTime");

                    if (timeProperty == null)
                        return Time.realtimeSinceStartupAsDouble;

                    var raw = timeProperty.GetValue(_runner);
                    if (raw is double asDouble)
                    {
                        _lastKnownNetworkTimeSeconds = asDouble;
                        return asDouble;
                    }

                    if (raw is float asFloat)
                    {
                        _lastKnownNetworkTimeSeconds = asFloat;
                        return asFloat;
                    }

                    var asDoubleProperty = raw?.GetType().GetProperty("AsDouble");
                    if (asDoubleProperty?.GetValue(raw) is double nestedAsDouble)
                    {
                        _lastKnownNetworkTimeSeconds = nestedAsDouble;
                        return nestedAsDouble;
                    }

                    if (raw != null && double.TryParse(raw.ToString(), out var parsed))
                    {
                        _lastKnownNetworkTimeSeconds = parsed;
                        return parsed;
                    }
                }
                catch
                {
                }

                return ResolveFallbackTime();
            }
        }

        public async UniTask CreateHostSessionAsync(OnlineSessionConfig config)
        {
            EnsureNotDisposed();
            await UniTask.SwitchToMainThread();

            TryApplyFixedRegion(config.Region);

            var runner = await EnsureRunnerReadyAsync();
            var result = await runner.StartGame(new StartGameArgs
            {
                GameMode = GameMode.Host,
                SessionName = config.SessionId.Value,
            });

            if (!result.Ok)
                throw new PhotonSessionTransportException(
                    MapShutdownReason(result.ShutdownReason.ToString()),
                    $"Create host failed: {result.ShutdownReason}");

            _lastSessionName = config.SessionId.Value;
            _lastGameMode = GameMode.Host;

            RaiseLifecycle("host_created", config.SessionId.Value, config.HostUserId);
        }

        public async UniTask JoinSessionAsync(SessionId sessionId, string region, string currentUserId)
        {
            EnsureNotDisposed();

            if (string.IsNullOrWhiteSpace(region))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(region));

            if (string.IsNullOrWhiteSpace(currentUserId))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(currentUserId));

            await UniTask.SwitchToMainThread();

            TryApplyFixedRegion(region);

            var runner = await EnsureRunnerReadyAsync();
            var result = await runner.StartGame(new StartGameArgs
            {
                GameMode = GameMode.Client,
                SessionName = sessionId.Value,
            });

            if (!result.Ok)
                throw new PhotonSessionTransportException(
                    MapShutdownReason(result.ShutdownReason.ToString()),
                    $"Join failed: {result.ShutdownReason}");

            _lastSessionName = sessionId.Value;
            _lastGameMode = GameMode.Client;

            RaiseLifecycle("join_succeeded", sessionId.Value, currentUserId);
        }

        public async UniTask<PhotonTransportMatchmakingResult> JoinRandomOrCreateSessionAsync(MatchmakingRoomOptions options, CancellationToken ct)
        {
            EnsureNotDisposed();

            if (options == null)
                throw new ArgumentNullException(nameof(options));

            ct.ThrowIfCancellationRequested();

            await UniTask.SwitchToMainThread(ct);

            TryApplyFixedRegion(options.Region);

            var runner = await EnsureRunnerReadyAsync();
            var args = new StartGameArgs
            {
                GameMode = GameMode.AutoHostOrClient,
                EnableClientSessionCreation = true,
                PlayerCount = options.MaxPlayers,
                SessionProperties = BuildSessionProperties(options),
            };

            var result = await runner.StartGame(args);
            if (!result.Ok)
            {
                throw new PhotonSessionTransportException(
                    MapShutdownReason(result.ShutdownReason.ToString()),
                    $"JoinRandomOrCreate failed: {result.ShutdownReason}");
            }

            _lastSessionName = runner.SessionInfo.Name;
            if (string.IsNullOrWhiteSpace(_lastSessionName))
            {
                throw new PhotonSessionTransportException(
                    OnlineErrorCode.NetworkUnavailable,
                    "JoinRandomOrCreate succeeded but session name is empty.");
            }

            _lastGameMode = runner.IsServer ? GameMode.Host : GameMode.Client;

            var remotePlayer = GetRemotePlayerId(runner);
            var playerCount = CountPlayers(runner);

            RaiseLifecycle("matchmaking_entered", _lastSessionName, remotePlayer);
            return new PhotonTransportMatchmakingResult(_lastSessionName, playerCount, remotePlayer, runner.IsServer);
        }

        public async UniTask LeaveSessionAsync()
        {
            if (_isDisposed)
                return;

            await UniTask.SwitchToMainThread();

            var sessionName = _lastSessionName;
            await DisposeRunnerAsync();
            RaiseLifecycle("left_room", sessionName, null);
        }

        public async UniTask ReconnectAsync(string region, string currentUserId)
        {
            EnsureNotDisposed();

            if (string.IsNullOrWhiteSpace(region))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(region));

            if (string.IsNullOrWhiteSpace(currentUserId))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(currentUserId));

            await UniTask.SwitchToMainThread();

            if (string.IsNullOrWhiteSpace(_lastSessionName) || !_lastGameMode.HasValue)
                throw new InvalidOperationException("Reconnect requires previous session binding.");

            TryApplyFixedRegion(region);

            var runner = await EnsureRunnerReadyAsync();
            var reconnectResult = await runner.StartGame(new StartGameArgs
            {
                GameMode = _lastGameMode.Value,
                SessionName = _lastSessionName,
            });

            if (!reconnectResult.Ok)
            {
                throw new PhotonSessionTransportException(
                    MapShutdownReason(reconnectResult.ShutdownReason.ToString()),
                    $"Reconnect failed: {reconnectResult.ShutdownReason}");
            }

            if (!IsRunnerConnectedToServer(runner))
                throw new PhotonSessionTransportException(OnlineErrorCode.NetworkUnavailable, "Reconnect attempt did not restore server connection.");

            RaiseLifecycle("reconnect_succeeded", _lastSessionName, currentUserId);
        }

        private double ResolveFallbackTime()
        {
            if (_lastKnownNetworkTimeSeconds > 0d)
                return _lastKnownNetworkTimeSeconds;

            return Time.realtimeSinceStartupAsDouble;
        }

        private static bool IsRunnerConnectedToServer(NetworkRunner runner)
        {
            try
            {
                var connectedProperty = runner.GetType().GetProperty("IsConnectedToServer");
                if (connectedProperty?.GetValue(runner) is bool connected)
                    return connected;

                var inSessionProperty = runner.GetType().GetProperty("IsInSession");
                if (inSessionProperty?.GetValue(runner) is bool inSession)
                    return inSession;
            }
            catch
            {
            }

            return false;
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
                var hasRemoteRecipient = await WaitForRemoteRecipientAsync(_runner, RemoteRecipientReadyTimeout);
                if (!hasRemoteRecipient)
                    throw new InvalidOperationException("Reliable data send requires at least one remote player.");

                foreach (var player in _runner.ActivePlayers)
                {
                    if (player == _runner.LocalPlayer)
                        continue;

                    _runner.SendReliableDataToPlayer(player, GameplayReliableKey, payload);
                }

                return;
            }

            _runner.SendReliableDataToServer(GameplayReliableKey, payload);
        }

        private static async UniTask<bool> WaitForRemoteRecipientAsync(NetworkRunner runner, TimeSpan timeout)
        {
            var deadline = Time.realtimeSinceStartupAsDouble + timeout.TotalSeconds;

            while (runner != null && runner.IsRunning)
            {
                foreach (var player in runner.ActivePlayers)
                {
                    if (player != runner.LocalPlayer)
                        return true;
                }

                if (Time.realtimeSinceStartupAsDouble >= deadline)
                    return false;

                await UniTask.Delay(RemoteRecipientPollDelay);
            }

            return false;
        }

        private async UniTask<NetworkRunner> EnsureRunnerReadyAsync()
        {
            if (_runner != null)
            {
                await DisposeRunnerAsync();
            }

            var go = new GameObject(RunnerObjectName);
            DontDestroyOnLoad(go);
            _runner = go.AddComponent<NetworkRunner>();
            _runner.ProvideInput = false;
            _runner.AddCallbacks(this);
            return _runner;
        }

        private static Dictionary<string, SessionProperty> BuildSessionProperties(MatchmakingRoomOptions options)
        {
            return new Dictionary<string, SessionProperty>
            {
                ["gameId"] = options.GameId,
                ["ph"] = options.ParamsHash,
            };
        }

        private static int CountPlayers(NetworkRunner runner)
        {
            var count = 0;
            foreach (var _ in runner.ActivePlayers)
                count++;

            return count;
        }

        private static string? GetRemotePlayerId(NetworkRunner runner)
        {
            foreach (var player in runner.ActivePlayers)
            {
                if (player == runner.LocalPlayer)
                    continue;

                return player.ToString();
            }

            return null;
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

            if (runner == null)
                return;

            runner.RemoveCallbacks(this);

            if (runner != null)
            {
                var runnerGameObject = runner.gameObject;
                if (runnerGameObject != null)
                    Destroy(runnerGameObject);
            }
        }

        private void RaiseLifecycle(string kind, string? sessionId, string? userId) =>
            LifecycleEvent?.Invoke(new PhotonTransportLifecycleEvent(kind, sessionId, userId));

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

            if (ContainsAny(shutdownReason, "AlreadyInGame", "InProgress"))
                return OnlineErrorCode.SessionAlreadyInGame;

            if (ContainsAny(shutdownReason, "InvalidAuthentication", "AuthenticationFailed", "Auth"))
                return OnlineErrorCode.NetworkUnavailable;

            return OnlineErrorCode.NetworkUnavailable;
        }

        private static bool ContainsAny(string source, params string[] values)
        {
            for (var i = 0; i < values.Length; i++)
            {
                if (source.IndexOf(values[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        private void EnsureNotDisposed()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(FusionSessionTransport));
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

            if (runner != null)
                runner.RemoveCallbacks(this);

            if (runner != null && runner.IsRunning)
                runner.Shutdown();

            if (runner != null)
            {
                var runnerGameObject = runner.gameObject;
                if (runnerGameObject != null)
                    Destroy(runnerGameObject);
            }
        }

        void INetworkRunnerCallbacks.OnPlayerJoined(NetworkRunner runner, PlayerRef player)
        {
            if (!runner.IsRunning)
                return;

            if (player == runner.LocalPlayer)
                return;

            RaiseLifecycle("peer_joined", _lastSessionName, player.ToString());
        }

        void INetworkRunnerCallbacks.OnPlayerLeft(NetworkRunner runner, PlayerRef player) =>
            RaiseLifecycle("peer_left", _lastSessionName, player.ToString());

        void INetworkRunnerCallbacks.OnConnectedToServer(NetworkRunner runner) =>
            RaiseLifecycle("connected", _lastSessionName, null);

        void INetworkRunnerCallbacks.OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) =>
            RaiseLifecycle("disconnected", _lastSessionName, reason.ToString());

        void INetworkRunnerCallbacks.OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason) =>
            RaiseLifecycle("shutdown", _lastSessionName, shutdownReason.ToString());

        void INetworkRunnerCallbacks.OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) =>
            RaiseLifecycle("connect_failed", _lastSessionName, reason.ToString());

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
            if (key != GameplayReliableKey)
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

#nullable restore
