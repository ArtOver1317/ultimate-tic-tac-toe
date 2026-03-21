using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using R3;
using Runtime.GameModes.Wizard;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.Coordinator;
using Runtime.GameModes.Wizard.Matchmaking.Config;
using Runtime.GameModes.Wizard.Matchmaking.Runtime;
using Runtime.GameModes.Wizard.Modes;
using Runtime.GameModes.Wizard.Online;
using Runtime.GameModes.Wizard.Session;

namespace Tests.EditMode.GameModes.Wizard.Coordinator
{
    [TestFixture]
    [Category("Unit")]
    public partial class GameWizardCoordinatorTests
    {
        private SpyWizardNavigator _navigator;
        private SessionFactorySpy _sessionFactory;
        private GameWizardCoordinator _sut;

        [SetUp]
        public void SetUp()
        {
            _navigator = new SpyWizardNavigator();
            _sessionFactory = new SessionFactorySpy();
            _sut = new GameWizardCoordinator(_navigator, _sessionFactory.Create);
        }

        [TearDown]
        public void TearDown()
        {
            _sut?.Dispose();
            _sut = null;
        }

        private sealed class SpyWizardNavigator : IGameWizardNavigator
        {
            private readonly object _lock = new();

            public TaskCompletionSource<bool> CloseAllCalled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public Func<CancellationToken, UniTask> OpenModeSelectionImpl;
            public Func<CancellationToken, UniTask> CloseModeSelectionImpl;
            public readonly Func<CancellationToken, UniTask> OpenMatchSetupImpl;
            public readonly Func<CancellationToken, UniTask> CloseMatchSetupImpl;
            public readonly Func<CancellationToken, UniTask<MatchmakingViewModel>> OpenMatchmakingImpl;
            public readonly Func<CancellationToken, UniTask> CloseMatchmakingImpl;
            public readonly Func<CancellationToken, UniTask> ReplaceModeSelectionWithMatchSetupImpl;
            public readonly Func<CancellationToken, UniTask> ReplaceMatchSetupWithModeSelectionImpl;
            public readonly Func<CancellationToken, UniTask<MatchmakingViewModel>> ReplaceMatchSetupWithMatchmakingImpl;
            public readonly Func<CancellationToken, UniTask> ReplaceMatchmakingWithMatchSetupImpl;
            public Func<CancellationToken, UniTask> CloseAllImpl;

            public int OpenModeSelectionCalls { get; private set; }
            public int CloseModeSelectionCalls { get; private set; }
            public int OpenMatchSetupCalls { get; private set; }
            public int CloseMatchSetupCalls { get; private set; }
            public int OpenMatchmakingCalls { get; private set; }
            public int CloseMatchmakingCalls { get; private set; }
            public int ReplaceModeSelectionWithMatchSetupCalls { get; private set; }
            public int ReplaceMatchSetupWithModeSelectionCalls { get; private set; }
            public int ReplaceMatchSetupWithMatchmakingCalls { get; private set; }
            public int ReplaceMatchmakingWithMatchSetupCalls { get; private set; }
            public int CloseAllCalls { get; private set; }

            public List<string> CallHistory { get; } = new();

            public SpyWizardNavigator()
            {
                OpenModeSelectionImpl = _ => UniTask.CompletedTask;
                CloseModeSelectionImpl = _ => UniTask.CompletedTask;
                OpenMatchSetupImpl = _ => UniTask.CompletedTask;
                CloseMatchSetupImpl = _ => UniTask.CompletedTask;
                
                OpenMatchmakingImpl = _ => UniTask.FromException<MatchmakingViewModel>(
                    new InvalidOperationException("SpyWizardNavigator.OpenMatchmakingAsync is not configured."));
               
                CloseMatchmakingImpl = _ => UniTask.CompletedTask;
                ReplaceModeSelectionWithMatchSetupImpl = _ => UniTask.CompletedTask;
                ReplaceMatchSetupWithModeSelectionImpl = _ => UniTask.CompletedTask;
               
                ReplaceMatchSetupWithMatchmakingImpl = _ => UniTask.FromException<MatchmakingViewModel>(
                    new InvalidOperationException("SpyWizardNavigator.ReplaceMatchSetupWithMatchmakingAsync is not configured."));
               
                ReplaceMatchmakingWithMatchSetupImpl = _ => UniTask.CompletedTask;
                CloseAllImpl = _ => UniTask.CompletedTask;
            }

            public UniTask OpenModeSelectionAsync(CancellationToken ct)
            {
                lock (_lock)
                {
                    OpenModeSelectionCalls++;
                    CallHistory.Add(nameof(OpenModeSelectionAsync));
                }

                return OpenModeSelectionImpl(ct);
            }

            public UniTask CloseModeSelectionAsync(CancellationToken ct)
            {
                lock (_lock)
                {
                    CloseModeSelectionCalls++;
                    CallHistory.Add(nameof(CloseModeSelectionAsync));
                }

                return CloseModeSelectionImpl(ct);
            }

            public UniTask OpenMatchSetupAsync(CancellationToken ct)
            {
                lock (_lock)
                {
                    OpenMatchSetupCalls++;
                    CallHistory.Add(nameof(OpenMatchSetupAsync));
                }

                return OpenMatchSetupImpl(ct);
            }

            public UniTask CloseMatchSetupAsync(CancellationToken ct)
            {
                lock (_lock)
                {
                    CloseMatchSetupCalls++;
                    CallHistory.Add(nameof(CloseMatchSetupAsync));
                }

                return CloseMatchSetupImpl(ct);
            }

            public UniTask<MatchmakingViewModel> OpenMatchmakingAsync(CancellationToken ct)
            {
                lock (_lock)
                {
                    OpenMatchmakingCalls++;
                    CallHistory.Add(nameof(OpenMatchmakingAsync));
                }

                return OpenMatchmakingImpl(ct);
            }

            public UniTask CloseMatchmakingAsync(CancellationToken ct)
            {
                lock (_lock)
                {
                    CloseMatchmakingCalls++;
                    CallHistory.Add(nameof(CloseMatchmakingAsync));
                }

                return CloseMatchmakingImpl(ct);
            }

            public UniTask ReplaceModeSelectionWithMatchSetupAsync(CancellationToken ct)
            {
                lock (_lock)
                {
                    ReplaceModeSelectionWithMatchSetupCalls++;
                    CallHistory.Add(nameof(ReplaceModeSelectionWithMatchSetupAsync));
                }

                return ReplaceModeSelectionWithMatchSetupImpl(ct);
            }

            public UniTask ReplaceMatchSetupWithModeSelectionAsync(CancellationToken ct)
            {
                lock (_lock)
                {
                    ReplaceMatchSetupWithModeSelectionCalls++;
                    CallHistory.Add(nameof(ReplaceMatchSetupWithModeSelectionAsync));
                }

                return ReplaceMatchSetupWithModeSelectionImpl(ct);
            }

            public UniTask<MatchmakingViewModel> ReplaceMatchSetupWithMatchmakingAsync(CancellationToken ct)
            {
                lock (_lock)
                {
                    ReplaceMatchSetupWithMatchmakingCalls++;
                    CallHistory.Add(nameof(ReplaceMatchSetupWithMatchmakingAsync));
                }

                return ReplaceMatchSetupWithMatchmakingImpl(ct);
            }

            public UniTask ReplaceMatchmakingWithMatchSetupAsync(CancellationToken ct)
            {
                lock (_lock)
                {
                    ReplaceMatchmakingWithMatchSetupCalls++;
                    CallHistory.Add(nameof(ReplaceMatchmakingWithMatchSetupAsync));
                }

                return ReplaceMatchmakingWithMatchSetupImpl(ct);
            }

            public UniTask CloseAllWizardWindowsAsync(CancellationToken ct)
            {
                lock (_lock)
                {
                    CloseAllCalls++;
                    CallHistory.Add(nameof(CloseAllWizardWindowsAsync));
                }

                CloseAllCalled.TrySetResult(true);

                return CloseAllImpl(ct);
            }
        }

        private sealed class SpyOnlineSessionFlow : IOnlineSessionFlowService
        {
            private readonly ReactiveProperty<OnlineFlowSnapshot> _snapshot = new(
                new OnlineFlowSnapshot(
                    OnlineFlowState.Idle,
                    previousStableState: null,
                    candidateSessionId: string.Empty,
                    activeSessionId: null,
                    flowEpoch: 1,
                    region: string.Empty,
                    canStart: false,
                    isBusy: false,
                    errorCode: OnlineErrorCode.None,
                    errorLocalizationKey: null,
                    statusLocalizationKey: null,
                    countdownRemainingSeconds: null,
                    graceDeadlineUtc: null));

            public int EnterHumanSetupCalls { get; private set; }
            public int JoinBySessionIdCalls { get; private set; }
            public int ExitCalls { get; private set; }

            public ReadOnlyReactiveProperty<OnlineFlowSnapshot> Snapshot => _snapshot;

            public UniTask EnterHumanSetupAsync(string region, string currentUserId)
            {
                EnterHumanSetupCalls++;
                return UniTask.CompletedTask;
            }

            public UniTask JoinBySessionIdAsync(string rawSessionIdInput, string region, string currentUserId)
            {
                JoinBySessionIdCalls++;
                return UniTask.CompletedTask;
            }

            public UniTask ConfirmHostIntentAsync() => UniTask.CompletedTask;
            public UniTask StartHostSessionAsync(OnlineSessionConfig hostConfig) => UniTask.CompletedTask;
            public UniTask CopyVisibleSessionIdAsync() => UniTask.CompletedTask;
            public UniTask BackAsync() => UniTask.CompletedTask;
            
            public UniTask ExitAsync()
            {
                ExitCalls++;
                return UniTask.CompletedTask;
            }
           
            public UniTask SetReadyForNextMatchAsync(bool isReady) => UniTask.CompletedTask;
            public UniTask OnOpponentReadyForNextMatchAsync(bool isReady) => UniTask.CompletedTask;
            public UniTask OnHostCreatedAsync() => UniTask.CompletedTask;
            public UniTask OnJoinSucceededAsync() => UniTask.CompletedTask;
            public UniTask OnJoinFailedAsync(OnlineErrorCode errorCode) => UniTask.CompletedTask;
            public UniTask OnGuestJoinedAsync() => UniTask.CompletedTask;
            public UniTask OnCountdownTickAsync(int remainingSeconds) => UniTask.CompletedTask;
            public UniTask OnGameplayEnteredAsync() => UniTask.CompletedTask;
            public UniTask OnRoundCompletedAsync() => UniTask.CompletedTask;
            public UniTask OnDisconnectDetectedAsync() => UniTask.CompletedTask;
            public UniTask OnReconnectSucceededAsync() => UniTask.CompletedTask;
            public UniTask OnGraceTimeoutAsync(int eventEpoch) => UniTask.CompletedTask;
            public UniTask OnOpponentLeftAsync() => UniTask.CompletedTask;

            public void Dispose() { }
        }

        private sealed class SessionFactorySpy
        {
            public readonly List<FakeGameSession> CreatedSessions = new();
            public Exception ThrowOnCreate;
            public bool ReturnNull;

            public IGameSession Create()
            {
                if (ThrowOnCreate != null)
                    throw ThrowOnCreate;

                if (ReturnNull)
                    return null;

                var session = new FakeGameSession();
                CreatedSessions.Add(session);
                return session;
            }
        }

        private sealed class FakeGameSession : IGameSession
        {
            private readonly ReactiveProperty<GameSessionSnapshot> _snapshot =
                new(GameSessionSnapshot.Default);

            private readonly ReactiveProperty<bool> _canStart = new(false);
            private readonly ReactiveProperty<IReadOnlyList<ValidationError>> _validationErrors = new(Array.Empty<ValidationError>());

            public int DisposeCallCount { get; private set; }
            public TaskCompletionSource<bool> Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public ReadOnlyReactiveProperty<GameSessionSnapshot> Snapshot => _snapshot;
            public ReadOnlyReactiveProperty<bool> CanStart => _canStart;
            public ReadOnlyReactiveProperty<IReadOnlyList<ValidationError>> ValidationErrors => _validationErrors;

            public void Update(Func<GameSessionSnapshot, GameSessionSnapshot> reducer) =>
                _snapshot.Value = reducer(_snapshot.Value);

            public void SetModeConfig(IGameConfig config) => throw new NotSupportedException();

            public Result<GameLaunchConfig> BuildLaunchConfig()
            {
                var snapshot = _snapshot.Value;

                var gameId = string.IsNullOrWhiteSpace(snapshot.SelectedGameId)
                    ? TicTacToeStrategy.DefaultGameId
                    : snapshot.SelectedGameId;

                var gameConfig = snapshot.GameConfig ?? new TicTacToeConfig(3);

                IOpponentConfig opponentConfig = snapshot.OpponentType switch
                {
                    OpponentType.Bot => new BotOpponentConfig(snapshot.BotDifficultyId ?? "Easy"),
                    OpponentType.Human => snapshot.HumanOpponentKind switch
                    {
                        HumanOpponentKind.Local => new LocalHumanConfig(),
                        HumanOpponentKind.DirectInvite => new DirectInviteConfig(snapshot.TargetPlayerId ?? "AB2CD7"),
                        HumanOpponentKind.Matchmaking => new MatchmakingConfig("Match", "Opponent"),
                        _ => new LocalHumanConfig(),
                    },
                    _ => new LocalHumanConfig(),
                };

                return Result<GameLaunchConfig>.Success(new GameLaunchConfig(gameId, gameConfig, opponentConfig));
            }

            public void Reset() => _snapshot.Value = GameSessionSnapshot.Default;

            public void Dispose()
            {
                DisposeCallCount++;
                Disposed.TrySetResult(true);
            }
        }
    }
}