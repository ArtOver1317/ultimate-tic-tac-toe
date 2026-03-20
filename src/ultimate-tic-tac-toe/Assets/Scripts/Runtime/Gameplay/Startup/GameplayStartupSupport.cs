#nullable enable
using System;
using R3;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.Online;
using Runtime.Gameplay.ECS.Lifecycle;
using Runtime.Games.Battleship.AI;
using Runtime.Games.Battleship.Core;
using Runtime.Games.Battleship.Networking;
using Runtime.Games.Battleship.Placement;
using Runtime.Games.Battleship.UI.Board;
using Runtime.Games.Battleship.UI.Placement;
using Runtime.Games.TicTacToe;
using Runtime.Games.TicTacToe.AI.Core;
using Runtime.Games.TicTacToe.AI.Ultimate.Core;
using Runtime.Games.TicTacToe.Series;
using Runtime.Games.TicTacToe.Ultimate;
using Runtime.Games.TicTacToe.Ultimate.Rules;
using Runtime.Games.TicTacToe.Ultimate.UI;
using Runtime.Infrastructure.GameStateMachine;
using Runtime.Localization;
using Runtime.Localization.Contracts;
using Runtime.PlayerProfile;
using Runtime.PlayerStatistics;

namespace Runtime.Gameplay.Startup
{
    internal sealed class GameplayStartupUiState
    {
        internal readonly MiniBoardStatus[] UltimateMiniBoardBuffer = new MiniBoardStatus[UltimateBoardConstants.MajorCount];

        internal FieldRenderSpec? FieldSpec;
        internal GameResultViewModel? ResultViewModel;
        internal UltimateAllowedBinder? UltimateAllowedBinder;
        internal UltimateMiniBoardStatusBinder? UltimateMiniBoardStatusBinder;
        internal CompositeDisposable? Subscriptions;
        internal bool OnlinePlayerNamesStoreBound;
    }

    internal sealed class GameplayStartupBotState
    {
        internal bool ClassicBotStarted;
        internal bool BattleshipBotStarted;
        internal bool UltimateBotStarted;
    }

    internal sealed class GameplayStartupOnlineState
    {
        internal bool IsOnlineDirectInvite;
        internal bool OnlineIsHost;
        internal bool OnlineRoundFinished;
        internal bool OnlineRematchStarted;
        internal bool OnlineTerminalResultShown;
        internal bool UseHostAuthoritativeFilter;
        internal string? OnlineLocalUserId;
        internal string? OnlineRemoteUserId;
        internal long OnlineAcceptedShotSequence;
    }

    internal sealed class GameplayStartupMatchState
    {
        internal bool RestartInProgress;
        internal int ExitToMenuRequested;
        internal GameLaunchConfig? ActiveLaunchConfig;
        internal bool Disposed;
    }

    internal sealed class GameplayStartupBattleshipState
    {
        internal bool IsBattleshipMatch;
        internal int BattleshipCurrentStartingSlot = -1;
        internal bool BattleshipRecoveryHeartbeatStarted;
    }

    internal sealed class GameplayStartupRuntimeState
    {
        internal GameplayStartupUiState Ui { get; } = new();
        internal GameplayStartupBotState Bot { get; } = new();
        internal GameplayStartupOnlineState Online { get; } = new();
        internal GameplayStartupMatchState Match { get; } = new();
        internal GameplayStartupBattleshipState Battleship { get; } = new();
    }

    internal sealed class GameplayStartupCoreServices
    {
        internal readonly IGameLaunchConfigStore ConfigStore;
        internal readonly IGameService GameService;
        internal readonly IGameplayFieldPresenter FieldPresenter;
        internal readonly IGameplayFieldUiAdapter FieldUiAdapter;
        internal readonly IMatchEcsLifecycle EcsLifecycle;
        internal readonly IGameplayEventStream EventStream;
        internal readonly IGameplayCommandSink CommandSink;
        internal readonly GameplayMovesBinder MovesBinder;
        internal readonly WinLineRenderer WinLineRenderer;
        internal readonly ISeriesService SeriesService;
        internal readonly IMatchPlayerNames? MatchPlayerNames;
        internal readonly IGameplayBackHandler BackHandler;
        internal readonly IGameStateMachine StateMachine;
        internal readonly ILocalizationService? Localization;
        internal readonly PlayerStatisticsMatchReporter? StatisticsReporter;

        public GameplayStartupCoreServices(
            IGameLaunchConfigStore configStore,
            IGameService gameService,
            IGameplayFieldPresenter fieldPresenter,
            IGameplayFieldUiAdapter fieldUiAdapter,
            IMatchEcsLifecycle ecsLifecycle,
            IGameplayEventStream eventStream,
            IGameplayCommandSink commandSink,
            GameplayMovesBinder movesBinder,
            WinLineRenderer winLineRenderer,
            ISeriesService seriesService,
            IGameplayBackHandler backHandler,
            IGameStateMachine stateMachine,
            ILocalizationService? localization = null,
            IMatchPlayerNames? matchPlayerNames = null,
            PlayerStatisticsMatchReporter? statisticsReporter = null)
        {
            ConfigStore = configStore;
            GameService = gameService;
            FieldPresenter = fieldPresenter;
            FieldUiAdapter = fieldUiAdapter;
            EcsLifecycle = ecsLifecycle;
            EventStream = eventStream;
            CommandSink = commandSink;
            MovesBinder = movesBinder;
            WinLineRenderer = winLineRenderer;
            SeriesService = seriesService;
            MatchPlayerNames = matchPlayerNames;
            BackHandler = backHandler;
            StateMachine = stateMachine;
            Localization = localization;
            StatisticsReporter = statisticsReporter;
        }
    }

    internal sealed class GameplayStartupTimerServices
    {
        internal readonly IMoveTimerService MoveTimerService;
        internal readonly MoveTimerHudBinder? MoveTimerHudBinder;
        internal readonly IBattleshipPlacementTimerService BattleshipPlacementTimerService;
        internal readonly BattleshipPlacementTimerHudBinder? BattleshipPlacementTimerHudBinder;

        public GameplayStartupTimerServices(
            IMoveTimerService moveTimerService,
            IBattleshipPlacementTimerService battleshipPlacementTimerService,
            MoveTimerHudBinder? moveTimerHudBinder = null,
            BattleshipPlacementTimerHudBinder? battleshipPlacementTimerHudBinder = null)
        {
            MoveTimerService = moveTimerService;
            MoveTimerHudBinder = moveTimerHudBinder;
            BattleshipPlacementTimerService = battleshipPlacementTimerService;
            BattleshipPlacementTimerHudBinder = battleshipPlacementTimerHudBinder;
        }
    }

    internal sealed class GameplayStartupBotServices
    {
        internal readonly IBotTurnDriver BotDriver;
        internal readonly IBattleshipBotDriver? BattleshipBotDriver;
        internal readonly IBotTurnOrchestrator UltimateBotOrchestrator;
        internal readonly IMatchFailSafeGateway MatchFailSafeGateway;
        internal readonly IUltimateGameplaySnapshotProvider? UltimateSnapshotProvider;
        internal readonly IUltimateGameplayEventStream? UltimateEventStream;

        public GameplayStartupBotServices(
            IBotTurnDriver botDriver,
            IBattleshipBotDriver? battleshipBotDriver,
            IBotTurnOrchestrator ultimateBotOrchestrator,
            IMatchFailSafeGateway matchFailSafeGateway,
            IUltimateGameplaySnapshotProvider? ultimateSnapshotProvider = null,
            IUltimateGameplayEventStream? ultimateEventStream = null)
        {
            BotDriver = botDriver;
            BattleshipBotDriver = battleshipBotDriver;
            UltimateBotOrchestrator = ultimateBotOrchestrator;
            MatchFailSafeGateway = matchFailSafeGateway;
            UltimateSnapshotProvider = ultimateSnapshotProvider;
            UltimateEventStream = ultimateEventStream;
        }
    }

    internal sealed class GameplayStartupOnlineServices
    {
        internal readonly IGameplayNetworkBridge NetworkBridge;
        internal readonly IBattleshipNetworkBridge BattleshipNetworkBridge;
        internal readonly IOnlineGameplaySessionContextStore OnlineSessionContextStore;
        internal readonly IOnlineSessionFlowService OnlineSessionFlow;
        internal readonly IOnlineSessionLauncher OnlineSessionLauncher;
        internal readonly IOnlinePlayerNamesStore? OnlinePlayerNamesStore;
        internal readonly IMatchStateProvider? MatchStateProvider;
        internal readonly HostAuthoritativeMoveProcessor HostMoveProcessor = new();
        internal readonly OnlineRoundCoordinator OnlineRoundCoordinator = new();

        public GameplayStartupOnlineServices(
            IGameplayNetworkBridge networkBridge,
            IBattleshipNetworkBridge battleshipNetworkBridge,
            IOnlineGameplaySessionContextStore onlineSessionContextStore,
            IOnlineSessionFlowService onlineSessionFlow,
            IOnlineSessionLauncher onlineSessionLauncher,
            IOnlinePlayerNamesStore? onlinePlayerNamesStore = null,
            IMatchStateProvider? matchStateProvider = null)
        {
            NetworkBridge = networkBridge;
            BattleshipNetworkBridge = battleshipNetworkBridge;
            OnlineSessionContextStore = onlineSessionContextStore;
            OnlineSessionFlow = onlineSessionFlow;
            OnlineSessionLauncher = onlineSessionLauncher;
            OnlinePlayerNamesStore = onlinePlayerNamesStore;
            MatchStateProvider = matchStateProvider;
        }
    }

    internal sealed class GameplayStartupBattleshipServices
    {
        internal readonly BattleshipBoardsBinder? BattleshipBoardsBinder;
        internal readonly IBattleshipPlacementUiController? BattleshipPlacementUiController;
        internal readonly IBattleshipGameplaySnapshotProvider? BattleshipSnapshotProvider;
        internal readonly IBattleshipGameplayEventStream? BattleshipEventStream;
        internal readonly IBattleshipLayoutSerializer BattleshipLayoutSerializer;
        internal readonly IBattleshipRecoveryStateApplier? BattleshipRecoveryStateApplier;

        public GameplayStartupBattleshipServices(
            IBattleshipLayoutSerializer battleshipLayoutSerializer,
            BattleshipBoardsBinder? battleshipBoardsBinder = null,
            IBattleshipPlacementUiController? battleshipPlacementUiController = null,
            IBattleshipGameplaySnapshotProvider? battleshipSnapshotProvider = null,
            IBattleshipGameplayEventStream? battleshipEventStream = null,
            IBattleshipRecoveryStateApplier? battleshipRecoveryStateApplier = null)
        {
            BattleshipBoardsBinder = battleshipBoardsBinder;
            BattleshipPlacementUiController = battleshipPlacementUiController;
            BattleshipSnapshotProvider = battleshipSnapshotProvider;
            BattleshipEventStream = battleshipEventStream;
            BattleshipLayoutSerializer = battleshipLayoutSerializer;
            BattleshipRecoveryStateApplier = battleshipRecoveryStateApplier;
        }
    }

    internal sealed class GameplayStartupDependencies
    {
        internal GameplayStartupCoreServices Core { get; }
        internal GameplayStartupTimerServices Timers { get; }
        internal GameplayStartupBotServices Bot { get; }
        internal GameplayStartupOnlineServices Online { get; }
        internal GameplayStartupBattleshipServices Battleship { get; }

        public GameplayStartupDependencies(
            GameplayStartupCoreServices core,
            GameplayStartupTimerServices timers,
            GameplayStartupBotServices bot,
            GameplayStartupOnlineServices online,
            GameplayStartupBattleshipServices battleship)
        {
            Core = core ?? throw new ArgumentNullException(nameof(core));
            Timers = timers ?? throw new ArgumentNullException(nameof(timers));
            Bot = bot ?? throw new ArgumentNullException(nameof(bot));
            Online = online ?? throw new ArgumentNullException(nameof(online));
            Battleship = battleship ?? throw new ArgumentNullException(nameof(battleship));
        }
    }
}