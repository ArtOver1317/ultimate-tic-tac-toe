#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using R3;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.Matchmaking.Contracts;
using Runtime.GameModes.Wizard.Modes;
using Runtime.GameModes.Wizard.Online;
using Runtime.GameModes.Wizard.Online.Flow;
using Runtime.PlayerProfile;

namespace Tests.EditMode.GameModes.Wizard.Online.Launcher
{
    [TestFixture]
    [Category("Integration")]
    public partial class OnlineSessionLauncherTests
    {
        private static GameLaunchConfig CreateDirectInviteConfig(string sessionId, IGameConfig gameConfig)
            => new("tic-tac-toe", gameConfig, new DirectInviteConfig(sessionId));

        private static async Task BringFlowToStateAsync(OnlineSessionFlowService flow, OnlineFlowState targetState)
        {
            await flow.EnterHumanSetupAsync("eu", "host");

            if (targetState == OnlineFlowState.Idle)
                return;

            if (targetState == OnlineFlowState.GuestConnecting)
            {
                await flow.JoinBySessionIdAsync("AB2CD7", "eu", "guest");
                return;
            }

            await flow.ConfirmHostIntentAsync();
            await flow.StartHostSessionAsync(new OnlineSessionConfig(new SessionId("ABCDEF"), "eu", "host"));

            if (targetState == OnlineFlowState.HostStarting)
                return;

            await flow.OnHostCreatedAsync();

            if (targetState == OnlineFlowState.WaitingForPlayer)
                return;

            await flow.OnGuestJoinedAsync();

            if (targetState == OnlineFlowState.ConnectedCountdown)
                return;

            await flow.OnGameplayEnteredAsync();

            if (targetState == OnlineFlowState.InGame)
                return;

            await flow.OnRoundCompletedAsync();
        }

        private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan? timeout = null)
        {
            var deadline = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromSeconds(3));
            
            while (DateTimeOffset.UtcNow < deadline)
            {
                if (predicate())
                    return;

                await UniTask.Delay(TimeSpan.FromMilliseconds(20));
            }

            Assert.Fail("Condition was not met within timeout.");
        }

        private static TestHarness CreateHarness(
            TimeSpan? reconnectGraceTimeout = null,
            TimeSpan? reconnectRetryDelay = null,
            string? customName = null)
        {
            const string localUserId = "tests-local-user";
            var lifecycle = new OnlineSessionIdLifecycle(() => "ABCDEF");
            var flow = new OnlineSessionFlowService(lifecycle);
            var gateway = new SpyPhotonSessionGateway();
            var transport = new SpyPhotonSessionTransport();
            var countdownSync = new OnlineCountdownSyncService();
            var contextStore = new OnlineGameplaySessionContextStore();
            var diagnosticsBuffer = new OnlineDiagnosticsBuffer();
            var cleanupTracker = new OnlineCleanupTracker();
            var playerNameService = new FakePlayerNameService(new PlayerNameSnapshot(customName, customName ?? "Player"));

            var launcher = new OnlineSessionLauncher(
                gateway,
                transport,
                flow,
                countdownSync,
                contextStore,
                diagnosticsBuffer,
                cleanupTracker,
                playerNameService,
                localUserId,
                reconnectGraceTimeout ?? TimeSpan.FromSeconds(30),
                reconnectRetryDelay ?? TimeSpan.FromSeconds(1));

            return new TestHarness(
                launcher,
                flow,
                gateway,
                transport,
                contextStore,
                diagnosticsBuffer,
                cleanupTracker,
                localUserId);
        }

        private sealed class FakePlayerNameService : IPlayerNameService
        {
            private readonly ReactiveProperty<PlayerNameSnapshot> _snapshot;

            public FakePlayerNameService(PlayerNameSnapshot snapshot) => 
                _snapshot = new ReactiveProperty<PlayerNameSnapshot>(snapshot);

            public ReadOnlyReactiveProperty<PlayerNameSnapshot> Snapshot => _snapshot;

            public UniTask<PlayerNameChangeResult> TryChangeNameAsync(string? requestedName, CancellationToken ct)
                => UniTask.FromResult(PlayerNameChangeResult.Success());
        }

        private sealed class TestHarness : IDisposable
        {
            public TestHarness(
                OnlineSessionLauncher launcher,
                OnlineSessionFlowService flow,
                SpyPhotonSessionGateway gateway,
                SpyPhotonSessionTransport transport,
                OnlineGameplaySessionContextStore contextStore,
                OnlineDiagnosticsBuffer diagnosticsBuffer,
                OnlineCleanupTracker cleanupTracker,
                string localUserId)
            {
                Launcher = launcher;
                Flow = flow;
                Gateway = gateway;
                Transport = transport;
                ContextStore = contextStore;
                DiagnosticsBuffer = diagnosticsBuffer;
                CleanupTracker = cleanupTracker;
                LocalUserId = localUserId;
            }

            public OnlineSessionLauncher Launcher { get; }
            public OnlineSessionFlowService Flow { get; }
            public SpyPhotonSessionGateway Gateway { get; }
            public SpyPhotonSessionTransport Transport { get; }
            public OnlineGameplaySessionContextStore ContextStore { get; }
            public OnlineDiagnosticsBuffer DiagnosticsBuffer { get; }
            public OnlineCleanupTracker CleanupTracker { get; }
            public string LocalUserId { get; }

            public void Dispose()
            {
                Launcher.Dispose();
                Flow.Dispose();
                Gateway.Dispose();
                Transport.Dispose();
            }
        }

        private sealed class SpyPhotonSessionGateway : IPhotonSessionGateway
        {
            private readonly ReactiveProperty<GatewayLifecycleEvent?> _lifecycle = new(null);

            public int CreateHostCallCount { get; private set; }
            public int JoinCallCount { get; private set; }
            public int LeaveCallCount { get; private set; }
            public int TryReconnectCallCount { get; private set; }
            public double NetworkTimeSecondsValue { get; set; }
            public Func<double>? NetworkTimeSecondsProvider { get; set; }

            public Func<OnlineSessionConfig, UniTask<GatewayOperationResult>>? CreateHostSessionAsyncImpl { get; set; }
            public Func<SessionId, string, string, UniTask<GatewayOperationResult>>? JoinSessionAsyncImpl { get; set; }
            public Func<string, string, UniTask<GatewayOperationResult>>? TryReconnectAsyncImpl { get; set; }
            public Func<UniTask>? LeaveSessionAsyncImpl { get; set; }

            public ReadOnlyReactiveProperty<GatewayLifecycleEvent?> LifecycleEvent => _lifecycle;
            
            public double NetworkTimeSeconds => NetworkTimeSecondsProvider != null
                ? NetworkTimeSecondsProvider()
                : NetworkTimeSecondsValue;

            public UniTask<GatewayOperationResult> CreateHostSessionAsync(OnlineSessionConfig config)
            {
                CreateHostCallCount++;
                return CreateHostSessionAsyncImpl?.Invoke(config) ?? UniTask.FromResult(GatewayOperationResult.Success());
            }

            public UniTask<GatewayOperationResult> JoinSessionAsync(SessionId sessionId, string region, string currentUserId)
            {
                JoinCallCount++;
                return JoinSessionAsyncImpl?.Invoke(sessionId, region, currentUserId) ?? UniTask.FromResult(GatewayOperationResult.Success());
            }

            public UniTask LeaveSessionAsync()
            {
                LeaveCallCount++;
                return LeaveSessionAsyncImpl?.Invoke() ?? UniTask.CompletedTask;
            }

            public UniTask<GatewayOperationResult> TryReconnectAsync(string region, string currentUserId)
            {
                TryReconnectCallCount++;
                return TryReconnectAsyncImpl?.Invoke(region, currentUserId) ?? UniTask.FromResult(GatewayOperationResult.Success());
            }

            public void RaiseLifecycleEvent(string kind, string? sessionId, string? userId)
                => _lifecycle.Value = new GatewayLifecycleEvent(kind, sessionId, userId);

            public void Dispose() => _lifecycle.Dispose();
        }

        private sealed class SpyPhotonSessionTransport : IPhotonSessionTransport, IDisposable
        {
            private readonly List<string> _sentPayloads = new();

            public event Action<PhotonTransportLifecycleEvent>? LifecycleEvent;
            public event Action<PhotonReliableDataEvent>? ReliableDataReceived;

            public IReadOnlyList<string> SentPayloads => _sentPayloads;

            public double NetworkTimeSeconds => 0d;
            public bool IsInSession => true;
            public bool IsServerRole => true;

            public UniTask CreateHostSessionAsync(OnlineSessionConfig config) => UniTask.CompletedTask;

            public UniTask JoinSessionAsync(SessionId sessionId, string region, string currentUserId) => UniTask.CompletedTask;

            public UniTask<PhotonTransportMatchmakingResult> JoinRandomOrCreateSessionAsync(MatchmakingRoomOptions options, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                return UniTask.FromResult(new PhotonTransportMatchmakingResult("room", 1, null, isHost: true));
            }

            public UniTask LeaveSessionAsync() => UniTask.CompletedTask;

            public UniTask ReconnectAsync(string region, string currentUserId) => UniTask.CompletedTask;

            public UniTask SendReliableDataAsync(byte[] payload)
            {
                _sentPayloads.Add(Encoding.UTF8.GetString(payload));
                return UniTask.CompletedTask;
            }

            public void RaiseReliableData(byte[] payload) => ReliableDataReceived?.Invoke(new PhotonReliableDataEvent(payload));

            public void Dispose()
            {
                LifecycleEvent = null;
                ReliableDataReceived = null;
                _sentPayloads.Clear();
            }
        }
    }
}