using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.Modes;
using Runtime.Infrastructure.GameStateMachine.States;
using Runtime.Localization.Types;
using Runtime.UI.MainMenu;
using Runtime.UI.Settings;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.EditMode.UI.MainMenu
{
    public partial class MainMenuCoordinatorTests
    {
        [Test]
        public async Task WhenSettingsRequestedFromMenu_ThenOpensSettingsWindow()
        {
            var settingsVm = new SettingsViewModel(_localizationMock);
            var settingsView = CreateInactiveSettingsView(settingsVm);
            _uiServiceMock.Open<SettingsView, SettingsViewModel>().Returns(settingsView);

            _coordinator.Initialize(_viewModel);

            _viewModel.RequestSettings();
            await UniTask.Yield();

            _uiServiceMock.Received(1).Open<SettingsView, SettingsViewModel>();
        }

        [Test]
        public async Task WhenStatisticsRequestedFromMenu_ThenOpensStatisticsWindow()
        {
            var statisticsService = Substitute.For<Runtime.PlayerStatistics.IPlayerStatisticsService>();
            statisticsService.GetEntriesSnapshot().Returns(Array.Empty<Runtime.PlayerStatistics.StatisticsEntry>());

            var strategy = Substitute.For<IGameStrategy>();
            strategy.GameId.Returns("tic-tac-toe");
           
            strategy.Metadata.Returns(new GameMetadata(
                id: "tic-tac-toe",
                displayNameKey: "Game.TicTacToe",
                descriptionKey: "Game.Description.TicTacToe",
                iconAssetKey: "icons/game_tic_tac_toe",
                sortOrder: 10,
                supportsBot: true,
                supportsOnline: true,
                supportsLocal: true));
           
            strategy.GetSupportedBotDifficultyIds().Returns(new[] { "Easy", "Normal", "Hard" });

            var gameCatalog = Substitute.For<IGameCatalog>();
            gameCatalog.Strategies.Returns(new[] { strategy });

            var botCatalog = Substitute.For<IBotDifficultyCatalog>();
            
            botCatalog.Difficulties.Returns(new[]
            {
                new BotDifficulty("Easy", "GameWizard.MatchSetup.BotDifficulty.Easy", 0),
                new BotDifficulty("Normal", "GameWizard.MatchSetup.BotDifficulty.Normal", 1),
                new BotDifficulty("Hard", "GameWizard.MatchSetup.BotDifficulty.Hard", 2),
            });

            var statisticsVm = new PlayerStatisticsViewModel(statisticsService, gameCatalog, botCatalog, _localizationMock);
            var statisticsView = CreateInactivePlayerStatisticsView(statisticsVm);
            _uiServiceMock.Open<PlayerStatisticsView, PlayerStatisticsViewModel>().Returns(statisticsView);

            _coordinator.Initialize(_viewModel);

            _viewModel.RequestStatistics();
            await UniTask.Yield();

            _uiServiceMock.Received(1).Open<PlayerStatisticsView, PlayerStatisticsViewModel>();
        }

        [Test]
        public async Task WhenStartGameRequested_ThenClosesOverlaysAndEntersGameplayState()
        {
            _coordinator.Initialize(_viewModel);
            var config = new GameLaunchConfig("Classic", new TicTacToeConfig(3), new LocalHumanConfig());

            _viewModel.RequestStartGame();
            await UniTask.Yield();

            _uiServiceMock.Received(1).Close<LanguageSelectionView>();
            _uiServiceMock.Received(1).Close<SettingsView>();
            await _wizardCoordinatorMock.Received(1).StartWizardAsync(Arg.Any<CancellationToken>());

            _gameLaunchRequested.OnNext(config);
            await UniTask.Yield();

            await _stateMachineMock.Received(1)
                .EnterAsync<LoadGameplayState, GameLaunchConfig>(config, Arg.Any<CancellationToken>());
        }

        [Test]
        public async Task WhenLanguageRequestedFromSettings_ThenOpensLanguageSelectionWindow()
        {
            var settingsVm = new SettingsViewModel(_localizationMock);
            var settingsView = CreateInactiveSettingsView(settingsVm);
            _uiServiceMock.Open<SettingsView, SettingsViewModel>().Returns(settingsView);

            var languageVm = new LanguageSelectionViewModel(_localizationMock);
            var languageView = CreateInactiveLanguageSelectionView(languageVm);
            _uiServiceMock.Open<LanguageSelectionView, LanguageSelectionViewModel>().Returns(languageView);

            _coordinator.Initialize(_viewModel);

            _viewModel.RequestSettings();
            await UniTask.Yield();
            settingsVm.OpenLanguageSelection();

            _uiServiceMock.Received(1).Open<LanguageSelectionView, LanguageSelectionViewModel>();
        }

        [Test]
        public async Task WhenPlayerNameEditRequestedFromSettings_ThenOpensPlayerNameEditWindow()
        {
            var settingsVm = new SettingsViewModel(_localizationMock);
            var settingsView = CreateInactiveSettingsView(settingsVm);
            _uiServiceMock.Open<SettingsView, SettingsViewModel>().Returns(settingsView);

            var playerNameVm = new PlayerNameEditViewModel(Substitute.For<Runtime.PlayerProfile.IPlayerNameService>(), _localizationMock);
            var playerNameView = CreateInactivePlayerNameEditView(playerNameVm);
            _uiServiceMock.Open<PlayerNameEditView, PlayerNameEditViewModel>().Returns(playerNameView);

            _coordinator.Initialize(_viewModel);

            _viewModel.RequestSettings();
            await UniTask.Yield();
            settingsVm.OpenPlayerNameEdit();
            await UniTask.Yield();

            _uiServiceMock.Received(1).Open<PlayerNameEditView, PlayerNameEditViewModel>();
        }

        [Test]
        public async Task WhenSettingsClosed_ThenLanguageRequestDoesNotOpenLanguageSelectionWindow()
        {
            var settingsVm = new SettingsViewModel(_localizationMock);
            var settingsView = CreateInactiveSettingsView(settingsVm);
            _uiServiceMock.Open<SettingsView, SettingsViewModel>().Returns(settingsView);

            _coordinator.Initialize(_viewModel);

            _viewModel.RequestSettings();
            await UniTask.Yield();
            settingsVm.Close();
            settingsVm.OpenLanguageSelection();

            _uiServiceMock.DidNotReceive().Open<LanguageSelectionView, LanguageSelectionViewModel>();
        }

        [Test]
        public async Task WhenCoordinatorDisposedAfterSettingsOpened_ThenSettingsActionsDoNotTriggerFurtherNavigation()
        {
            var settingsVm = new SettingsViewModel(_localizationMock);
            var settingsView = CreateInactiveSettingsView(settingsVm);
            _uiServiceMock.Open<SettingsView, SettingsViewModel>().Returns(settingsView);

            _coordinator.Initialize(_viewModel);

            _viewModel.RequestSettings();
            await UniTask.Yield();
            _coordinator.Dispose();

            settingsVm.OpenLanguageSelection();
            settingsVm.OpenPlayerNameEdit();
            await UniTask.Yield();

            _uiServiceMock.DidNotReceive().Open<LanguageSelectionView, LanguageSelectionViewModel>();
            _uiServiceMock.DidNotReceive().Open<PlayerNameEditView, PlayerNameEditViewModel>();
        }

        [Test]
        public async Task WhenSettingsRequestedTwiceBeforeFirstOpenCompletes_ThenSecondRequestIsIgnored()
        {
            var settingsVm = new SettingsViewModel(_localizationMock);
            var settingsView = CreateInactiveSettingsView(settingsVm);
            var preloadGate = new UniTaskCompletionSource();
            var preloadCallCount = 0;

            _uiServiceMock.Open<SettingsView, SettingsViewModel>().Returns(settingsView);

            _localizationMock.PreloadAsync(
                    Arg.Any<LocaleId>(),
                    Arg.Any<IReadOnlyList<TextTableId>>(),
                    Arg.Any<CancellationToken>())
                .Returns(_ =>
                {
                    var currentCall = Interlocked.Increment(ref preloadCallCount);
                    return currentCall == 1 ? preloadGate.Task : UniTask.CompletedTask;
                });

            _coordinator.Initialize(_viewModel);

            _viewModel.RequestSettings();
            _viewModel.RequestSettings();
            await UniTask.Yield();

            preloadCallCount.Should().Be(1);

            preloadGate.TrySetResult();
            await UniTask.Yield();

            _uiServiceMock.Received(1).Open<SettingsView, SettingsViewModel>();
        }

        [Test]
        public async Task WhenSettingsOpenThrows_ThenLogsErrorAndDoesNotThrow()
        {
            LogAssert.Expect(LogType.Error, new Regex(@"Failed to open SettingsView"));

            _uiServiceMock.Open<SettingsView, SettingsViewModel>()
                .Returns(_ => throw new InvalidOperationException("SettingsView open failed"));

            _coordinator.Initialize(_viewModel);

            _viewModel.Invoking(vm => vm.RequestSettings())
                .Should().NotThrow();

            await UniTask.Yield();
        }

        [Test]
        public async Task WhenLanguageSelectionOpenThrows_ThenLogsErrorAndDoesNotThrow()
        {
            LogAssert.Expect(LogType.Error, new Regex(@"Failed to open LanguageSelectionView"));
            var settingsVm = new SettingsViewModel(_localizationMock);
            var settingsView = CreateInactiveSettingsView(settingsVm);
            _uiServiceMock.Open<SettingsView, SettingsViewModel>().Returns(settingsView);

            _uiServiceMock.Open<LanguageSelectionView, LanguageSelectionViewModel>()
                .Returns(_ => throw new InvalidOperationException("LanguageSelectionView open failed"));

            _coordinator.Initialize(_viewModel);

            _viewModel.RequestSettings();
            await UniTask.Yield();
            _viewModel.Invoking(_ => settingsVm.OpenLanguageSelection()).Should().NotThrow();
        }
    }
}