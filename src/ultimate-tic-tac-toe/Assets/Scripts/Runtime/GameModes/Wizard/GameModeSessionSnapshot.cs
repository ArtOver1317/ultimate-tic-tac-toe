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
        public int Version { get; }

        private GameModeSessionSnapshot(
            string? selectedModeId,
            IGameModeConfig? modeConfig,
            OpponentType opponentType,
            string? botDifficultyId,
            HumanOpponentKind humanOpponentKind,
            string? targetPlayerId,
            MatchmakingState matchmakingState,
            int version)
        {
            SelectedModeId = selectedModeId;
            ModeConfig = modeConfig;
            OpponentType = opponentType;
            BotDifficultyId = botDifficultyId;
            HumanOpponentKind = humanOpponentKind;
            TargetPlayerId = targetPlayerId;
            MatchmakingState = matchmakingState;
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
            int version) =>
            new GameModeSessionSnapshot(
                selectedModeId,
                modeConfig,
                opponentType,
                botDifficultyId,
                humanOpponentKind,
                targetPlayerId,
                matchmakingState,
                version);

        public static GameModeSessionSnapshot Default => Create(
            selectedModeId: null,
            modeConfig: null,
            opponentType: OpponentType.Bot,
            botDifficultyId: null,
            humanOpponentKind: HumanOpponentKind.Local,
            targetPlayerId: null,
            matchmakingState: MatchmakingState.Idle,
            version: 0);

        public GameModeSessionSnapshot WithSelectedModeId(string? selectedModeId) =>
            new GameModeSessionSnapshot(selectedModeId, ModeConfig, OpponentType, BotDifficultyId, HumanOpponentKind, TargetPlayerId, MatchmakingState, Version);

        public GameModeSessionSnapshot WithModeConfig(IGameModeConfig? modeConfig) =>
            new GameModeSessionSnapshot(SelectedModeId, modeConfig, OpponentType, BotDifficultyId, HumanOpponentKind, TargetPlayerId, MatchmakingState, Version);

        public GameModeSessionSnapshot WithOpponentType(OpponentType opponentType) =>
            new GameModeSessionSnapshot(SelectedModeId, ModeConfig, opponentType, BotDifficultyId, HumanOpponentKind, TargetPlayerId, MatchmakingState, Version);

        public GameModeSessionSnapshot WithBotDifficultyId(string? botDifficultyId) =>
            new GameModeSessionSnapshot(SelectedModeId, ModeConfig, OpponentType, botDifficultyId, HumanOpponentKind, TargetPlayerId, MatchmakingState, Version);

        public GameModeSessionSnapshot WithHumanOpponentKind(HumanOpponentKind humanOpponentKind) =>
            new GameModeSessionSnapshot(SelectedModeId, ModeConfig, OpponentType, BotDifficultyId, humanOpponentKind, TargetPlayerId, MatchmakingState, Version);

        public GameModeSessionSnapshot WithTargetPlayerId(string? targetPlayerId) =>
            new GameModeSessionSnapshot(SelectedModeId, ModeConfig, OpponentType, BotDifficultyId, HumanOpponentKind, targetPlayerId, MatchmakingState, Version);

        public GameModeSessionSnapshot WithMatchmakingState(MatchmakingState matchmakingState) =>
            new GameModeSessionSnapshot(SelectedModeId, ModeConfig, OpponentType, BotDifficultyId, HumanOpponentKind, TargetPlayerId, matchmakingState, Version);

        public GameModeSessionSnapshot WithVersion(int version) =>
            new GameModeSessionSnapshot(SelectedModeId, ModeConfig, OpponentType, BotDifficultyId, HumanOpponentKind, TargetPlayerId, MatchmakingState, version);
    }

    public enum OpponentType
    {
        Bot,
        Human
    }

    public enum HumanOpponentKind
    {
        Local,
        DirectInvite,
        Matchmaking
    }

    public enum MatchmakingState
    {
        Idle,
        Searching,
        Found,
        Failed,
        Cancelled
    }
}

#nullable restore
