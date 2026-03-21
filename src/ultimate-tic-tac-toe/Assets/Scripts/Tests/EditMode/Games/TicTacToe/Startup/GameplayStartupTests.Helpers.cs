using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.Online;
using Runtime.Gameplay;
using Runtime.Gameplay.Startup;
using Runtime.Games.Battleship.Networking;
using Runtime.Games.Battleship.Placement;
using Runtime.Games.Battleship.Startup;
using Runtime.Games.TicTacToe;
using Runtime.Games.TicTacToe.AI.Ultimate.Core;
using Runtime.Games.TicTacToe.Ultimate;
using Runtime.Localization.Contracts;
using Runtime.PlayerProfile;
using Runtime.PlayerStatistics;
using UnityEngine;

namespace Tests.EditMode.Games.TicTacToe.Startup
{
    public partial class GameplayStartupTests
    {
        private static async Task RunAllowingFailingLogsAsync(Func<Task> action, params Regex[] expectedFailingLogs)
        {
            var captured = new List<(LogType type, string condition)>();

            void Handler(string condition, string stackTrace, LogType type)
            {
                if (type is LogType.Error or LogType.Exception or LogType.Assert)
                    captured.Add((type, condition));
            }

            var previousIgnore = UnityEngine.TestTools.LogAssert.ignoreFailingMessages;
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            Application.logMessageReceived += Handler;

            try
            {
                await action();
            }
            finally
            {
                Application.logMessageReceived -= Handler;
                UnityEngine.TestTools.LogAssert.ignoreFailingMessages = previousIgnore;
            }

            var messages = captured.Select(x => x.condition).ToList();
          
            messages.Count.Should().Be(expectedFailingLogs.Length,
                "Unexpected number of failing Unity logs was captured.");

            for (var i = 0; i < expectedFailingLogs.Length; i++)
            {
                var regex = expectedFailingLogs[i];
              
                regex.IsMatch(messages[i]).Should().BeTrue(
                    $"expected failing log #{i + 1} to match regex '{regex}', but was: {messages[i]}");
            }
        }

        private TicTacToeGameplayStartup CreateStartup(
            IMatchFailSafeGateway matchFailSafeGateway,
            IUltimateGameplaySnapshotProvider ultimateSnapshotProvider = null,
            IGameplayNetworkBridge networkBridge = null,
            IOnlineGameplaySessionContextStore onlineSessionContextStore = null,
            IMatchStateProvider matchStateProvider = null,
            IOnlineSessionFlowService onlineSessionFlow = null,
            IOnlineSessionLauncher onlineSessionLauncher = null,
            IOnlinePlayerNamesStore onlinePlayerNamesStore = null,
            ILocalizationService localization = null,
            IMoveTimerService moveTimerService = null,
            MoveTimerHudBinder moveTimerHudBinder = null,
            IMatchPlayerNames matchPlayerNames = null,
            PlayerStatisticsMatchReporter statisticsReporter = null,
            IUltimateGameplayEventStream ultimateEventStream = null)
        {
            var resolvedMatchStateProvider = matchStateProvider ?? _commandSink as IMatchStateProvider;
           
            var core = new GameplayStartupCoreServices(
                _configStore,
                _gameService,
                _fieldPresenter,
                _fieldUiAdapter,
                _ecsLifecycle,
                _eventStream,
                _commandSink,
                _movesBinder,
                _winLineRenderer,
                _seriesService,
                _backHandler,
                _stateMachine,
                localization,
                matchPlayerNames,
                statisticsReporter);
        
            var timers = new GameplayStartupTimerServices(
                moveTimerService ?? Substitute.For<IMoveTimerService>(),
                Substitute.For<IBattleshipPlacementTimerService>(),
                moveTimerHudBinder);
           
            var bot = new GameplayStartupBotServices(
                _botDriver,
                null,
                _ultimateBotOrchestrator,
                matchFailSafeGateway,
                ultimateSnapshotProvider,
                ultimateEventStream);
            
            var online = new GameplayStartupOnlineServices(
                networkBridge ?? new NoOpGameplayNetworkBridge(),
                NoOpBattleshipNetworkBridge.Instance,
                onlineSessionContextStore ?? new OnlineGameplaySessionContextStore(),
                onlineSessionFlow ?? NoOpOnlineSessionFlowService.Instance,
                onlineSessionLauncher ?? NoOpOnlineSessionLauncher.Instance,
                onlinePlayerNamesStore,
                resolvedMatchStateProvider);
          
            var battleship = new GameplayStartupBattleshipServices(new BattleshipLayoutSerializer());
            var dependencies = new GameplayStartupDependencies(core, timers, bot, online, battleship);
            var state = new GameplayStartupRuntimeState();
            var uiCoordinator = new GameplayStartupUiCoordinator(dependencies, state);
            var botCoordinator = new GameplayStartupBotCoordinator(dependencies, state);
            var sessionScoreStore = new GameplayStartupBattleshipSessionScoreStore();
          
            var recoveryCoordinator = new GameplayStartupBattleshipRecoveryCoordinator(
                dependencies,
                state,
                uiCoordinator,
                botCoordinator,
                sessionScoreStore);
          
            var onlineCoordinator = new GameplayStartupOnlineCoordinator(
                dependencies,
                state,
                uiCoordinator,
                recoveryCoordinator,
                sessionScoreStore);
          
            var roundCoordinator = new GameplayStartupRoundCoordinator(
                dependencies,
                state,
                uiCoordinator,
                sessionScoreStore);

            return new TicTacToeGameplayStartup(
                dependencies,
                state,
                uiCoordinator,
                botCoordinator,
                onlineCoordinator,
                roundCoordinator);
        }

        private static PlayerStatisticsMatchReporter CreateStatisticsReporter(
            IGameLaunchConfigStore configStore,
            IGameplayEventStream eventStream)
        {
            var outcomeResolver = Substitute.For<IMatchOutcomeResolver>();
            var statisticsService = Substitute.For<IPlayerStatisticsService>();
            var contextStore = Substitute.For<IOnlineGameplaySessionContextStore>();
            contextStore.Snapshot.Returns(OnlineGameplaySessionSnapshot.Empty());

            return new PlayerStatisticsMatchReporter(
                configStore,
                eventStream,
                outcomeResolver,
                statisticsService,
                contextStore,
                new MatchKeyMapper());
        }
    }
}