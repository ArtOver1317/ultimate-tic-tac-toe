#nullable enable

using System;
using System.Collections.Generic;
using R3;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.Matchmaking.Config;
using Runtime.GameModes.Wizard.Modes;
using Runtime.GameModes.Wizard.Online;

namespace Runtime.GameModes.Wizard.Session
{
    /// <summary>
    /// Default implementation of <see cref="IGameSession"/>.
    /// Thread-safe: all mutations are serialized by an internal lock.
    /// </summary>
    public sealed class GameSession : IGameSession
    {
        private static readonly IReadOnlyList<ValidationError> _noErrors = Array.Empty<ValidationError>();

        private readonly object _lock = new();
        private readonly IGameCatalog? _catalog;
        private readonly ReactiveProperty<GameSessionSnapshot> _snapshot;
        private readonly ReactiveProperty<bool> _canStart;
        private readonly ReactiveProperty<IReadOnlyList<ValidationError>> _validationErrors;

        private bool _isDisposed;

        public ReadOnlyReactiveProperty<GameSessionSnapshot> Snapshot => _snapshot;
        public ReadOnlyReactiveProperty<bool> CanStart => _canStart;
        public ReadOnlyReactiveProperty<IReadOnlyList<ValidationError>> ValidationErrors => _validationErrors;

        public GameSession() : this(catalog: null, initialSnapshot: GameSessionSnapshot.Default, isInternalCall: true) { }

        public GameSession(GameSessionSnapshot initialSnapshot)
            : this(catalog: null, initialSnapshot: initialSnapshot, isInternalCall: true) { }

        public GameSession(IGameCatalog catalog)
            : this(catalog ?? throw new ArgumentNullException(nameof(catalog)), GameSessionSnapshot.Default, isInternalCall: true) { }

        public GameSession(IGameCatalog catalog, GameSessionSnapshot initialSnapshot)
            : this(catalog ?? throw new ArgumentNullException(nameof(catalog)), initialSnapshot, isInternalCall: true) { }

        private GameSession(IGameCatalog? catalog, GameSessionSnapshot initialSnapshot, bool isInternalCall)
        {
            if (initialSnapshot == null)
                throw new ArgumentNullException(nameof(initialSnapshot));

            _catalog = catalog;

            var normalized = Normalize(initialSnapshot);

            _snapshot = new ReactiveProperty<GameSessionSnapshot>(normalized);
            _canStart = new ReactiveProperty<bool>(false);
            _validationErrors = new ReactiveProperty<IReadOnlyList<ValidationError>>(_noErrors);

            Recalculate(normalized);
        }

        public void Update(Func<GameSessionSnapshot, GameSessionSnapshot> reducer)
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

        public void SetModeConfig(IGameConfig config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            lock (_lock)
            {
                EnsureNotDisposed();

                if (ReferenceEquals(_snapshot.Value.GameConfig, config))
                    return;
            }

            Update(s => s.WithGameConfig(config));
        }

        public Result<GameLaunchConfig> BuildLaunchConfig()
        {
            EnsureNotDisposed();

            GameSessionSnapshot snapshot;

            lock (_lock)
            {
                EnsureNotDisposed();
                snapshot = _snapshot.Value;
            }

            var errors = ValidateForStart(snapshot);

            if (errors.Count > 0)
                return Result<GameLaunchConfig>.Failure(errors);

            var opponentConfig = BuildOpponentConfig(snapshot);
            
            if (opponentConfig == null)
            {
                return Result<GameLaunchConfig>.Failure(
                    new ValidationError(WizardFieldNames.Matchmaking, "Errors.GameWizard.MatchmakingConfigMissing"));
            }

            return Result<GameLaunchConfig>.Success(new GameLaunchConfig(
                gameId: snapshot.SelectedGameId ?? throw new InvalidOperationException("Selected mode is missing after validation."),
                gameConfig: snapshot.GameConfig ?? throw new InvalidOperationException("Mode config is missing after validation."),
                opponentConfig: opponentConfig,
                moveTimeLimitSeconds: snapshot.MoveTimeLimitSeconds));
        }

        private static IOpponentConfig? BuildOpponentConfig(GameSessionSnapshot snapshot) =>
            snapshot.OpponentType switch
            {
                OpponentType.Bot => string.IsNullOrWhiteSpace(snapshot.BotDifficultyId)
                    ? throw new InvalidOperationException("Bot difficulty is missing after validation.")
                    : new BotOpponentConfig(snapshot.BotDifficultyId),
                OpponentType.Human => BuildHumanOpponentConfig(snapshot),
                _ => throw new ArgumentOutOfRangeException(nameof(snapshot.OpponentType), snapshot.OpponentType, null),
            };

        private static IOpponentConfig? BuildHumanOpponentConfig(GameSessionSnapshot snapshot)
        {
            switch (snapshot.HumanOpponentKind)
            {
                case HumanOpponentKind.Local:
                    return new LocalHumanConfig();

                case HumanOpponentKind.DirectInvite:
                    return string.IsNullOrWhiteSpace(snapshot.TargetPlayerId) 
                        ? throw new InvalidOperationException("DirectInvite requires SessionId after validation.") 
                        : new DirectInviteConfig(snapshot.TargetPlayerId);

                case HumanOpponentKind.Matchmaking:
                    if (string.IsNullOrWhiteSpace(snapshot.MatchmakingMatchId) ||
                        string.IsNullOrWhiteSpace(snapshot.MatchmakingOpponentId))
                        return null;

                    return new MatchmakingConfig(snapshot.MatchmakingMatchId, snapshot.MatchmakingOpponentId, snapshot.MatchmakingIsHost);

                default:
                    throw new ArgumentOutOfRangeException(nameof(snapshot.HumanOpponentKind), snapshot.HumanOpponentKind, null);
            }
        }

        public void Reset()
        {
            lock (_lock)
            {
                EnsureNotDisposed();

                var current = _snapshot.Value;
                var reset = GameSessionSnapshot.Default.WithVersion(checked(current.Version + 1));
                var normalized = Normalize(reset);

                _snapshot.Value = normalized;
                Recalculate(normalized);
            }
        }

        public void Dispose()
        {
            IDisposable snapshotToDispose;
            IDisposable canStartToDispose;
            IDisposable errorsToDispose;

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
                throw new ObjectDisposedException(nameof(GameSession));
        }

        private void Recalculate(GameSessionSnapshot snapshot)
        {
            var errors = ValidateForStart(snapshot);

            _validationErrors.Value = errors.Count == 0 ? _noErrors : errors;
            _canStart.Value = errors.Count == 0;
        }

        private static GameSessionSnapshot Normalize(GameSessionSnapshot snapshot)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));

            var s = snapshot;

            if (s.OpponentType == OpponentType.Bot)
            {
                // Do not force a specific HumanOpponentKind when Bot is selected.
                // Keep the last chosen human kind to preserve UX when toggling back.
                s = s
                    .WithTargetPlayerId(null)
                    .WithMatchmakingState(MatchmakingState.Idle)
                    .WithMatchmakingResult(null, null);
            }
            else
            {
                // Human opponent
                if (s.HumanOpponentKind != HumanOpponentKind.DirectInvite)
                    s = s.WithTargetPlayerId(null);

                if (s.HumanOpponentKind != HumanOpponentKind.Matchmaking)
                {
                    s = s.WithMatchmakingState(MatchmakingState.Idle)
                        .WithMatchmakingResult(null, null);
                }
            }

            return s;
        }

        private IReadOnlyList<ValidationError> ValidateForStart(GameSessionSnapshot snapshot)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));

            List<ValidationError>? errors = null;

            ValidateGame(snapshot, ref errors);
            ValidateOpponent(snapshot, ref errors);

            return errors ?? _noErrors;
        }

        private void ValidateGame(GameSessionSnapshot snapshot, ref List<ValidationError>? errors)
        {
            if (string.IsNullOrWhiteSpace(snapshot.SelectedGameId))
                (errors ??= new List<ValidationError>(capacity: 4)).Add(new ValidationError(WizardFieldNames.SelectedGameId, "Errors.GameWizard.GameRequired"));

            if (snapshot.GameConfig == null)
                (errors ??= new List<ValidationError>(capacity: 4)).Add(new ValidationError(WizardFieldNames.GameConfig, "Errors.GameWizard.ConfigRequired"));

            if (string.IsNullOrWhiteSpace(snapshot.SelectedGameId))
                return;

            if (_catalog == null)
            {
                (errors ??= new List<ValidationError>(capacity: 4))
                    .Add(new ValidationError(WizardFieldNames.GameCatalog, "Errors.GameWizard.GameCatalogMissing"));
                
                return;
            }

            if (!_catalog.TryGetStrategy(snapshot.SelectedGameId, out var strategy) || strategy == null)
            {
                (errors ??= new List<ValidationError>(capacity: 4)).Add(new ValidationError(WizardFieldNames.SelectedGameId, "Errors.GameWizard.GameUnknown"));
                return;
            }

            if (strategy is IGameStartValidator startValidator)
            {
                var startErrors = startValidator.ValidateForStart(snapshot);
                
                if (startErrors is { Count: > 0 })
                {
                    errors ??= new List<ValidationError>(capacity: 4);
                    errors.AddRange(startErrors);
                }
            }

            if (snapshot.GameConfig == null)
                return;

            var modeErrors = strategy.ValidateConfig(snapshot.GameConfig);
            
            if (modeErrors.Count > 0)
            {
                errors ??= new List<ValidationError>(capacity: 4);
                errors.AddRange(modeErrors);
            }
        }

        private static void ValidateOpponent(GameSessionSnapshot snapshot, ref List<ValidationError>? errors)
        {
            if (snapshot.OpponentType == OpponentType.Bot)
            {
                if (string.IsNullOrWhiteSpace(snapshot.BotDifficultyId))
                    (errors ??= new List<ValidationError>(capacity: 4)).Add(new ValidationError(WizardFieldNames.BotDifficultyId, "Errors.GameWizard.DifficultyRequired"));
                
                return;
            }

            if (snapshot.HumanOpponentKind != HumanOpponentKind.DirectInvite)
                return;

            if (string.IsNullOrWhiteSpace(snapshot.TargetPlayerId))
            {
                (errors ??= new List<ValidationError>(capacity: 4))
                    .Add(new ValidationError(WizardFieldNames.InviteSessionId, "Errors.Online.InvalidSessionIdFormat"));
            }
            else if (!OnlineSessionIdFormatter.TryNormalizeToCanonical(snapshot.TargetPlayerId, out _))
            {
                (errors ??= new List<ValidationError>(capacity: 4))
                    .Add(new ValidationError(WizardFieldNames.InviteSessionId, "Errors.Online.InvalidSessionIdFormat"));
            }
        }
    }
}
