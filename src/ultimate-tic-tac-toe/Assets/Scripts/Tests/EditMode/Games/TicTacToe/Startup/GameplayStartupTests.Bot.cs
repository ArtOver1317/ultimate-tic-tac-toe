using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.Modes;
using Runtime.Gameplay;

namespace Tests.EditMode.Games.TicTacToe.Startup
{
    public partial class GameplayStartupTests
    {
        [Test]
        public async Task WhenUltimateBotMatchStarted_ThenStartsUltimateOrchestratorAndNotClassicDriver()
        {
            _config = new GameLaunchConfig(
                "ultimate",
                UltimateTicTacToeConfig.Instance,
                new BotOpponentConfig("Normal"));

            await _sut.StartAsync(CancellationToken.None);

            await _ultimateBotOrchestrator.Received(1)
                .StartAsync(1, "medium", Arg.Any<CancellationToken>());
            
            await _botDriver.DidNotReceive()
                .StartAsync(Arg.Any<GameLaunchConfig>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        }

        [Test]
        public async Task WhenClassicBotMatchStarted_ThenStartsClassicDriverAndNotUltimateOrchestrator()
        {
            _config = new GameLaunchConfig(
                "classic",
                new TicTacToeConfig(boardSize: 3, isUltimate: false),
                new BotOpponentConfig("Easy"));

            _botDriver.StartAsync(Arg.Any<GameLaunchConfig>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(UniTask.FromResult(new BotStartResult(BotStartStatus.Started)));

            await _sut.StartAsync(CancellationToken.None);

            await _botDriver.Received(1)
                .StartAsync(Arg.Any<GameLaunchConfig>(), 1, "Easy", Arg.Any<CancellationToken>());
            
            await _ultimateBotOrchestrator.DidNotReceive()
                .StartAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        }
    }
}