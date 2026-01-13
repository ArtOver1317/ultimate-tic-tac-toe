using System;
using System.Collections.Generic;
using R3;

namespace Runtime.GameModes.Wizard
{
    /// <summary>
    /// Default implementation of <see cref="IGameModeSession"/>.
    /// Thread-safe: all mutations are serialized by an internal lock.
    /// </summary>
    public sealed class GameModeSession : IGameModeSession
    {
        private static readonly IReadOnlyList<ValidationError> _noErrors = Array.Empty<ValidationError>();

        private readonly object _lock = new();
        private readonly ReactiveProperty<GameModeSessionSnapshot> _snapshot;
        private readonly ReactiveProperty<bool> _canStart;
        private readonly ReactiveProperty<IReadOnlyList<ValidationError>> _validationErrors;

        private bool _isDisposed;

        public ReadOnlyReactiveProperty<GameModeSessionSnapshot> Snapshot => _snapshot;
        public ReadOnlyReactiveProperty<bool> CanStart => _canStart;
        public ReadOnlyReactiveProperty<IReadOnlyList<ValidationError>> ValidationErrors => _validationErrors;

        public GameModeSession() : this(GameModeSessionSnapshot.Default)
        {
        }

        public GameModeSession(GameModeSessionSnapshot initialSnapshot)
        {
            if (initialSnapshot == null)
                throw new ArgumentNullException(nameof(initialSnapshot));

            var normalized = Normalize(initialSnapshot);

            _snapshot = new ReactiveProperty<GameModeSessionSnapshot>(normalized);
            _canStart = new ReactiveProperty<bool>(false);
            _validationErrors = new ReactiveProperty<IReadOnlyList<ValidationError>>(_noErrors);

            Recalculate(normalized);
        }

        public void Update(Func<GameModeSessionSnapshot, GameModeSessionSnapshot> reducer)
        {
            if (reducer == null)
                throw new ArgumentNullException(nameof(reducer));

            lock (_lock)
            {
                EnsureNotDisposed();

                var current = _snapshot.Value;
                var updated = reducer(current);

                if (updated == null)
                    throw new InvalidOperationException("Reducer returned null snapshot.");

                var normalized = Normalize(updated.WithVersion(checked(current.Version + 1)));

                _snapshot.Value = normalized;
                Recalculate(normalized);
            }
        }

        public void SetModeConfig(IGameModeConfig config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            Update(s => s.WithModeConfig(config));
        }

        public Result<GameLaunchConfig> BuildLaunchConfig()
        {
            EnsureNotDisposed();

            GameModeSessionSnapshot snapshot;

            lock (_lock)
            {
                EnsureNotDisposed();
                snapshot = _snapshot.Value;
            }

            var errors = ValidateForStart(snapshot);

            if (errors.Count > 0)
                return Result<GameLaunchConfig>.Failure(errors);

            if (string.IsNullOrWhiteSpace(snapshot.SelectedModeId))
                return Result<GameLaunchConfig>.Failure(new ValidationError("SelectedModeId", "error.mode_required"));

            if (snapshot.ModeConfig == null)
                return Result<GameLaunchConfig>.Failure(new ValidationError("ModeConfig", "error.mode_config_required"));

            IOpponentConfig opponentConfig;

            switch (snapshot.OpponentType)
            {
                case OpponentType.Bot:
                    if (string.IsNullOrWhiteSpace(snapshot.BotDifficultyId))
                        return Result<GameLaunchConfig>.Failure(new ValidationError("BotDifficultyId", "error.difficulty_required"));

                    opponentConfig = new BotOpponentConfig(snapshot.BotDifficultyId);
                    break;

                case OpponentType.Human:
                    switch (snapshot.HumanOpponentKind)
                    {
                        case HumanOpponentKind.Local:
                            opponentConfig = new LocalHumanConfig();
                            break;

                        case HumanOpponentKind.DirectInvite:
                            if (string.IsNullOrWhiteSpace(snapshot.TargetPlayerId))
                                return Result<GameLaunchConfig>.Failure(new ValidationError("TargetPlayerId", "error.player_id_required"));

                            opponentConfig = new DirectInviteConfig(snapshot.TargetPlayerId);
                            break;

                        case HumanOpponentKind.Matchmaking:
                            return Result<GameLaunchConfig>.Failure(
                                new ValidationError("Matchmaking", "error.matchmaking_config_missing"));

                        default:
                            throw new ArgumentOutOfRangeException(nameof(snapshot.HumanOpponentKind), snapshot.HumanOpponentKind, null);
                    }

                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(snapshot.OpponentType), snapshot.OpponentType, null);
            }

            return Result<GameLaunchConfig>.Success(new GameLaunchConfig(
                gameModeId: snapshot.SelectedModeId,
                modeConfig: snapshot.ModeConfig,
                opponentConfig: opponentConfig));
        }

        public void Reset()
        {
            lock (_lock)
            {
                EnsureNotDisposed();

                var current = _snapshot.Value;
                var reset = GameModeSessionSnapshot.Default.WithVersion(checked(current.Version + 1));
                var normalized = Normalize(reset);

                _snapshot.Value = normalized;
                Recalculate(normalized);
            }
        }

        public void Dispose()
        {
            IDisposable snapshotToDispose = null;
            IDisposable canStartToDispose = null;
            IDisposable errorsToDispose = null;

            lock (_lock)
            {
                if (_isDisposed)
                    return;

                _isDisposed = true;

                snapshotToDispose = _snapshot;
                canStartToDispose = _canStart;
                errorsToDispose = _validationErrors;
            }

            snapshotToDispose.Dispose();
            canStartToDispose.Dispose();
            errorsToDispose.Dispose();
        }

        private void EnsureNotDisposed()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(GameModeSession));
        }

        private void Recalculate(GameModeSessionSnapshot snapshot)
        {
            var errors = ValidateForStart(snapshot);

            _validationErrors.Value = errors.Count == 0 ? _noErrors : errors;
            _canStart.Value = errors.Count == 0;
        }

        private static GameModeSessionSnapshot Normalize(GameModeSessionSnapshot snapshot)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));

            var s = snapshot;

            if (s.OpponentType == OpponentType.Human)
                s = s.WithBotDifficultyId(null);

            if (s.OpponentType == OpponentType.Bot)
            {
                // Do not force a specific HumanOpponentKind when Bot is selected.
                // Keep the last chosen human kind to preserve UX when toggling back.
                s = s
                    .WithTargetPlayerId(null)
                    .WithMatchmakingState(MatchmakingState.Idle);
            }
            else
            {
                // Human opponent
                if (s.HumanOpponentKind != HumanOpponentKind.DirectInvite)
                    s = s.WithTargetPlayerId(null);

                if (s.HumanOpponentKind != HumanOpponentKind.Matchmaking)
                    s = s.WithMatchmakingState(MatchmakingState.Idle);
            }

            return s;
        }

        private static IReadOnlyList<ValidationError> ValidateForStart(GameModeSessionSnapshot snapshot)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));

            List<ValidationError> errors = null;

            if (string.IsNullOrWhiteSpace(snapshot.SelectedModeId))
                (errors ??= new List<ValidationError>(capacity: 4)).Add(new ValidationError("SelectedModeId", "error.mode_required"));

            if (snapshot.ModeConfig == null)
                (errors ??= new List<ValidationError>(capacity: 4)).Add(new ValidationError("ModeConfig", "error.mode_config_required"));

            if (snapshot.OpponentType == OpponentType.Bot)
            {
                if (string.IsNullOrWhiteSpace(snapshot.BotDifficultyId))
                    (errors ??= new List<ValidationError>(capacity: 4)).Add(new ValidationError("BotDifficultyId", "error.difficulty_required"));
            }
            else
            {
                if (snapshot.HumanOpponentKind == HumanOpponentKind.DirectInvite && string.IsNullOrWhiteSpace(snapshot.TargetPlayerId))
                    (errors ??= new List<ValidationError>(capacity: 4)).Add(new ValidationError("TargetPlayerId", "error.player_id_required"));

                // Phase 1: matchmaking resolution (MatchId/OpponentId) is not implemented.
                // Keep CanStart consistent with BuildLaunchConfig() by treating matchmaking as invalid for now.
                if (snapshot.HumanOpponentKind == HumanOpponentKind.Matchmaking)
                    (errors ??= new List<ValidationError>(capacity: 4)).Add(new ValidationError("Matchmaking", "error.matchmaking_config_missing"));
            }

            return errors ?? _noErrors;
        }
    }
}
