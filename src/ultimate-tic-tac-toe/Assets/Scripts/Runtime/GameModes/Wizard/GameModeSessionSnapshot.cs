#nullable enable

namespace Runtime.GameModes.Wizard
{
    /// <summary>
    /// Immutable snapshot of the current game mode wizard state.
    /// Acts as a single source of truth for UI.
    /// </summary>
    public sealed class GameModeSessionSnapshot
    {
        public string? SelectedModeId { get; }
        public IGameModeConfig? ModeConfig { get; }
        public OpponentType OpponentType { get; }
        public string? BotDifficultyId { get; }
        public HumanOpponentKind HumanOpponentKind { get; }
        public string? TargetPlayerId { get; }
        public MatchmakingState MatchmakingState { get; }
        public string? MatchmakingMatchId { get; }
        public string? MatchmakingOpponentId { get; }
        public int Version { get; }

        private GameModeSessionSnapshot(
            string? selectedModeId,
            IGameModeConfig? modeConfig,
            OpponentType opponentType,
            string? botDifficultyId,
            HumanOpponentKind humanOpponentKind,
            string? targetPlayerId,
            MatchmakingState matchmakingState,
            string? matchmakingMatchId,
            string? matchmakingOpponentId,
            int version)
        {
            SelectedModeId = selectedModeId;
            ModeConfig = modeConfig;
            OpponentType = opponentType;
            BotDifficultyId = botDifficultyId;
            HumanOpponentKind = humanOpponentKind;
            TargetPlayerId = targetPlayerId;
            MatchmakingState = matchmakingState;
            MatchmakingMatchId = matchmakingMatchId;
            MatchmakingOpponentId = matchmakingOpponentId;
            Version = version;
        }

        /// <summary>
        /// Factory for infrastructure and internal wiring.
        /// Prefer using <see cref="Default"/> and <c>With*</c> methods, and mutate state via <c>IGameModeSession.Update(...)</c>.
        /// </summary>
        internal static GameModeSessionSnapshot Create(
            string? selectedModeId,
            IGameModeConfig? modeConfig,
            OpponentType opponentType,
            string? botDifficultyId,
            HumanOpponentKind humanOpponentKind,
            string? targetPlayerId,
            MatchmakingState matchmakingState,
            string? matchmakingMatchId,
            string? matchmakingOpponentId,
            int version) =>
            new(
                selectedModeId,
                modeConfig,
                opponentType,
                botDifficultyId,
                humanOpponentKind,
                targetPlayerId,
                matchmakingState,
                matchmakingMatchId,
                matchmakingOpponentId,
                version);

        public static GameModeSessionSnapshot Default => Create(
            selectedModeId: null,
            modeConfig: null,
            opponentType: OpponentType.Bot,
            botDifficultyId: null,
            humanOpponentKind: HumanOpponentKind.Local,
            targetPlayerId: null,
            matchmakingState: MatchmakingState.Idle,
            matchmakingMatchId: null,
            matchmakingOpponentId: null,
            version: 0);

        public GameModeSessionSnapshot WithSelectedModeId(string? selectedModeId)
        {
            var isSameMode = string.Equals(SelectedModeId, selectedModeId, System.StringComparison.Ordinal);
            
            return new GameModeSessionSnapshot(
                selectedModeId,
                isSameMode ? ModeConfig : null,
                OpponentType,
                BotDifficultyId,
                HumanOpponentKind,
                TargetPlayerId,
                MatchmakingState,
                MatchmakingMatchId,
                MatchmakingOpponentId,
                Version);
        }

        public GameModeSessionSnapshot WithModeConfig(IGameModeConfig? modeConfig) =>
            new(SelectedModeId, modeConfig, OpponentType, BotDifficultyId, HumanOpponentKind, TargetPlayerId, MatchmakingState, MatchmakingMatchId, MatchmakingOpponentId, Version);

        public GameModeSessionSnapshot WithOpponentType(OpponentType opponentType) =>
            new(SelectedModeId, ModeConfig, opponentType, BotDifficultyId, HumanOpponentKind, TargetPlayerId, MatchmakingState, MatchmakingMatchId, MatchmakingOpponentId, Version);

        public GameModeSessionSnapshot WithBotDifficultyId(string? botDifficultyId) =>
            new(SelectedModeId, ModeConfig, OpponentType, botDifficultyId, HumanOpponentKind, TargetPlayerId, MatchmakingState, MatchmakingMatchId, MatchmakingOpponentId, Version);

        public GameModeSessionSnapshot WithHumanOpponentKind(HumanOpponentKind humanOpponentKind) =>
            new(SelectedModeId, ModeConfig, OpponentType, BotDifficultyId, humanOpponentKind, TargetPlayerId, MatchmakingState, MatchmakingMatchId, MatchmakingOpponentId, Version);

        public GameModeSessionSnapshot WithTargetPlayerId(string? targetPlayerId) =>
            new(SelectedModeId, ModeConfig, OpponentType, BotDifficultyId, HumanOpponentKind, targetPlayerId, MatchmakingState, MatchmakingMatchId, MatchmakingOpponentId, Version);

        public GameModeSessionSnapshot WithMatchmakingState(MatchmakingState matchmakingState) =>
            new(SelectedModeId, ModeConfig, OpponentType, BotDifficultyId, HumanOpponentKind, TargetPlayerId, matchmakingState, MatchmakingMatchId, MatchmakingOpponentId, Version);

        public GameModeSessionSnapshot WithMatchmakingResult(string? matchId, string? opponentId) =>
            new(SelectedModeId, ModeConfig, OpponentType, BotDifficultyId, HumanOpponentKind, TargetPlayerId, MatchmakingState, matchId, opponentId, Version);

        public GameModeSessionSnapshot WithVersion(int version) =>
            new(SelectedModeId, ModeConfig, OpponentType, BotDifficultyId, HumanOpponentKind, TargetPlayerId, MatchmakingState, MatchmakingMatchId, MatchmakingOpponentId, version);
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
        Found,
        Failed,
        Cancelled,
    }
}