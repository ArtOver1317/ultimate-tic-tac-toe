#nullable enable

namespace Runtime.GameModes.Wizard.Session
{
    /// <summary>
    /// Immutable snapshot of the current game mode wizard state.
    /// Acts as a single source of truth for UI.
    /// </summary>
    public sealed class GameSessionSnapshot
    {
        public string? SelectedGameId { get; }
        public IGameConfig? GameConfig { get; }
        public OpponentType OpponentType { get; }
        public string? BotDifficultyId { get; }
        public HumanOpponentKind HumanOpponentKind { get; }
        public string? TargetPlayerId { get; }
        public MatchmakingState MatchmakingState { get; }
        public string? MatchmakingMatchId { get; }
        public string? MatchmakingOpponentId { get; }
        public bool MatchmakingIsHost { get; }
        public int MoveTimeLimitSeconds { get; }
        public int Version { get; }

        private GameSessionSnapshot(
            string? selectedGameId,
            IGameConfig? gameConfig,
            OpponentType opponentType,
            string? botDifficultyId,
            HumanOpponentKind humanOpponentKind,
            string? targetPlayerId,
            MatchmakingState matchmakingState,
            string? matchmakingMatchId,
            string? matchmakingOpponentId,
            bool matchmakingIsHost,
            int moveTimeLimitSeconds,
            int version)
        {
            SelectedGameId = selectedGameId;
            GameConfig = gameConfig;
            OpponentType = opponentType;
            BotDifficultyId = botDifficultyId;
            HumanOpponentKind = humanOpponentKind;
            TargetPlayerId = targetPlayerId;
            MatchmakingState = matchmakingState;
            MatchmakingMatchId = matchmakingMatchId;
            MatchmakingOpponentId = matchmakingOpponentId;
            MatchmakingIsHost = matchmakingIsHost;
            MoveTimeLimitSeconds = moveTimeLimitSeconds >= 0 ? moveTimeLimitSeconds : 0;
            Version = version;
        }

        /// <summary>
        /// Factory for infrastructure and internal wiring.
        /// Prefer using <see cref="Default"/> and <c>With*</c> methods, and mutate state via <c>IGameSession.Update(...)</c>.
        /// </summary>
        internal static GameSessionSnapshot Create(
            string? selectedGameId,
            IGameConfig? gameConfig,
            OpponentType opponentType,
            string? botDifficultyId,
            HumanOpponentKind humanOpponentKind,
            string? targetPlayerId,
            MatchmakingState matchmakingState,
            string? matchmakingMatchId,
            string? matchmakingOpponentId,
            bool matchmakingIsHost,
            int moveTimeLimitSeconds,
            int version) =>
            new(
                selectedGameId,
                gameConfig,
                opponentType,
                botDifficultyId,
                humanOpponentKind,
                targetPlayerId,
                matchmakingState,
                matchmakingMatchId,
                matchmakingOpponentId,
                matchmakingIsHost,
                moveTimeLimitSeconds,
                version);

        public static GameSessionSnapshot Default => Create(
            selectedGameId: null,
            gameConfig: null,
            opponentType: OpponentType.Bot,
            botDifficultyId: null,
            humanOpponentKind: HumanOpponentKind.Local,
            targetPlayerId: null,
            matchmakingState: MatchmakingState.Idle,
            matchmakingMatchId: null,
            matchmakingOpponentId: null,
            matchmakingIsHost: false,
            moveTimeLimitSeconds: 0,
            version: 0);

        public GameSessionSnapshot WithSelectedGameId(string? selectedGameId)
        {
            var isSameMode = string.Equals(SelectedGameId, selectedGameId, System.StringComparison.Ordinal);
            
            return new GameSessionSnapshot(
                selectedGameId,
                isSameMode ? GameConfig : null,
                OpponentType,
                BotDifficultyId,
                HumanOpponentKind,
                TargetPlayerId,
                MatchmakingState,
                MatchmakingMatchId,
                MatchmakingOpponentId,
                MatchmakingIsHost,
                MoveTimeLimitSeconds,
                Version);
        }

        public GameSessionSnapshot WithGameConfig(IGameConfig? gameConfig) =>
            new(SelectedGameId, gameConfig, OpponentType, BotDifficultyId, HumanOpponentKind, TargetPlayerId, MatchmakingState, MatchmakingMatchId, MatchmakingOpponentId, MatchmakingIsHost, MoveTimeLimitSeconds, Version);

        public GameSessionSnapshot WithOpponentType(OpponentType opponentType) =>
            new(SelectedGameId, GameConfig, opponentType, BotDifficultyId, HumanOpponentKind, TargetPlayerId, MatchmakingState, MatchmakingMatchId, MatchmakingOpponentId, MatchmakingIsHost, MoveTimeLimitSeconds, Version);

        public GameSessionSnapshot WithBotDifficultyId(string? botDifficultyId) =>
            new(SelectedGameId, GameConfig, OpponentType, botDifficultyId, HumanOpponentKind, TargetPlayerId, MatchmakingState, MatchmakingMatchId, MatchmakingOpponentId, MatchmakingIsHost, MoveTimeLimitSeconds, Version);

        public GameSessionSnapshot WithHumanOpponentKind(HumanOpponentKind humanOpponentKind) =>
            new(SelectedGameId, GameConfig, OpponentType, BotDifficultyId, humanOpponentKind, TargetPlayerId, MatchmakingState, MatchmakingMatchId, MatchmakingOpponentId, MatchmakingIsHost, MoveTimeLimitSeconds, Version);

        public GameSessionSnapshot WithTargetPlayerId(string? targetPlayerId) =>
            new(SelectedGameId, GameConfig, OpponentType, BotDifficultyId, HumanOpponentKind, targetPlayerId, MatchmakingState, MatchmakingMatchId, MatchmakingOpponentId, MatchmakingIsHost, MoveTimeLimitSeconds, Version);

        public GameSessionSnapshot WithMatchmakingState(MatchmakingState matchmakingState) =>
            new(SelectedGameId, GameConfig, OpponentType, BotDifficultyId, HumanOpponentKind, TargetPlayerId, matchmakingState, MatchmakingMatchId, MatchmakingOpponentId, MatchmakingIsHost, MoveTimeLimitSeconds, Version);

        public GameSessionSnapshot WithMatchmakingResult(string? matchId, string? opponentId, bool isHost = false) =>
            new(SelectedGameId, GameConfig, OpponentType, BotDifficultyId, HumanOpponentKind, TargetPlayerId, MatchmakingState, matchId, opponentId, isHost, MoveTimeLimitSeconds, Version);

        public GameSessionSnapshot WithMoveTimeLimitSeconds(int moveTimeLimitSeconds) =>
            new(SelectedGameId, GameConfig, OpponentType, BotDifficultyId, HumanOpponentKind, TargetPlayerId, MatchmakingState, MatchmakingMatchId, MatchmakingOpponentId, MatchmakingIsHost, moveTimeLimitSeconds, Version);

        public GameSessionSnapshot WithVersion(int version) =>
            new(SelectedGameId, GameConfig, OpponentType, BotDifficultyId, HumanOpponentKind, TargetPlayerId, MatchmakingState, MatchmakingMatchId, MatchmakingOpponentId, MatchmakingIsHost, MoveTimeLimitSeconds, version);
    }

    public enum OpponentType
    {
        Bot,
        Human,
    }

    public enum HumanOpponentKind
    {
        Local,
        DirectInvite,
        Matchmaking,
    }

    public enum MatchmakingState
    {
        Idle,
        Searching,
        CancelPending,
        Found,
        TerminalModal,
        Failed,
        Cancelled,
    }
}