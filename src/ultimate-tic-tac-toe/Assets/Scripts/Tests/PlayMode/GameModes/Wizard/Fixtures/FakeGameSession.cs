using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.GameModes.Wizard;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.Coordinator;
using Runtime.GameModes.Wizard.Matchmaking;
using Runtime.GameModes.Wizard.Matchmaking.Config;
using Runtime.GameModes.Wizard.Modes;
using Runtime.GameModes.Wizard.Session;

namespace Tests.PlayMode.GameModes.Wizard
{
    /// <summary>
    /// Fake implementation of <see cref="IGameSession"/> for coordinator tests.
    /// Combines capabilities needed by both PlayMode and Matchmaking integration tests.
    /// </summary>
    internal sealed class FakeGameSession : IGameSession
    {
        private readonly ReactiveProperty<GameSessionSnapshot> _snapshot = new(GameSessionSnapshot.Default);
        private readonly ReactiveProperty<bool> _canStart = new(false);
        private readonly ReactiveProperty<IReadOnlyList<ValidationError>> _validationErrors = new(Array.Empty<ValidationError>());
        private bool _disposed;

        public bool ReturnFailureOnBuildLaunchConfig { get; set; }
        public int DisposeCallCount { get; private set; }

        public ReadOnlyReactiveProperty<GameSessionSnapshot> Snapshot => _snapshot;
        public ReadOnlyReactiveProperty<bool> CanStart => _canStart;
        public ReadOnlyReactiveProperty<IReadOnlyList<ValidationError>> ValidationErrors => _validationErrors;

        public void SetSnapshot(GameSessionSnapshot snapshot) => _snapshot.Value = snapshot;

        public void Update(Func<GameSessionSnapshot, GameSessionSnapshot> reducer) =>
            _snapshot.Value = reducer(_snapshot.Value);

        public void SetModeConfig(IGameConfig config) => throw new NotSupportedException();

        public Result<GameLaunchConfig> BuildLaunchConfig()
        {
            if (ReturnFailureOnBuildLaunchConfig)
            {
                return Result<GameLaunchConfig>.Failure(
                    new ValidationError("wizard.validation_failed", "Errors.GameWizard.UnhandledException"));
            }

            var snapshot = _snapshot.Value;

            var gameId = string.IsNullOrWhiteSpace(snapshot.SelectedGameId)
                ? TicTacToeStrategy.DefaultGameId
                : snapshot.SelectedGameId;

            var gameConfig = snapshot.GameConfig ?? new TicTacToeConfig(3);

            IOpponentConfig opponentConfig;

            switch (snapshot.OpponentType)
            {
                case OpponentType.Bot:
                    opponentConfig = new BotOpponentConfig(snapshot.BotDifficultyId ?? "Easy");
                    break;

                case OpponentType.Human:
                    switch (snapshot.HumanOpponentKind)
                    {
                        case HumanOpponentKind.Local:
                            opponentConfig = new LocalHumanConfig();
                            break;

                        case HumanOpponentKind.DirectInvite:
                            opponentConfig = new DirectInviteConfig(snapshot.TargetPlayerId ?? "AB2CD7");
                            break;

                        case HumanOpponentKind.Matchmaking:
                            opponentConfig = new MatchmakingConfig("Match", "Opponent");
                            break;

                        default:
                            opponentConfig = new LocalHumanConfig();
                            break;
                    }

                    break;

                default:
                    opponentConfig = new LocalHumanConfig();
                    break;
            }

            return Result<GameLaunchConfig>.Success(new GameLaunchConfig(gameId, gameConfig, opponentConfig));
        }

        public void Reset() => _snapshot.Value = GameSessionSnapshot.Default;

        public void Dispose()
        {
            DisposeCallCount++;

            if (_disposed) return;
            _disposed = true;

            _snapshot.Dispose();
            _canStart.Dispose();
            _validationErrors.Dispose();
        }
    }

    internal sealed class SessionFactorySpy
    {
        public readonly List<FakeGameSession> CreatedSessions = new();

        public IGameSession Create()
        {
            var session = new FakeGameSession();
            CreatedSessions.Add(session);
            return session;
        }
    }

    internal static class GameWizardCoordinatorTestExtensions
    {
        public static async UniTask TryAbortBestEffortAsync(this GameWizardCoordinator coordinator)
        {
            try
            {
                await coordinator.AbortWizardAsync(AbortReason.SceneChange);
            }
            catch
            {
                // Best-effort cleanup in tests.
            }
        }
    }
}
