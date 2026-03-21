#nullable enable

using System.Threading;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.Online;
using Runtime.Gameplay.Shared;
using Runtime.Gameplay.Startup;
using Runtime.Games.TicTacToe.Series;
using Runtime.Infrastructure.GameStateMachine.States;
using Tests.EditMode.Games.Battleship.Fakes;
using EcsGameStatus = Runtime.Gameplay.Shared.EcsGameStatus;

namespace Tests.EditMode.Games.Battleship.Startup
{
    [TestFixture]
    [Category("Unit")]
    public sealed class GameplayStartupBattleshipRoundTests
    {
        [Test]
        public async System.Threading.Tasks.Task WhenOnlyOnePlayerSignalsRoundReady_ThenRoundDoesNotRestart()
        {
            using var seriesService = new SeriesService();
            using var context = GameplayStartupBattleshipTestFactory.CreateContext(isHost: true, seriesService: seriesService);
            var localReadySubmitted = false;
            RoundReadySignal? localReadySignal = null;
      
            context.GameplayBridge
                .SubmitRoundReadyAsync(Arg.Any<RoundReadySignal>())
                .Returns(callInfo =>
                {
                    localReadySubmitted = true;
                    localReadySignal = callInfo.Arg<RoundReadySignal>();
                    return UniTask.CompletedTask;
                });
            
            using var sut = context.CreateSut();

            await sut.StartAsync(CancellationToken.None);

            context.RoundFinishedEvents.OnNext(new RoundFinishedEvent(EcsGameStatus.Timeout, PlayerSlotMapping.SlotX, winLine: null));
            await UniTask.DelayFrame(1);

            sut.HandleResultAction(ResultAction.Restart);
            await GameplayStartupBattleshipTestFactory.WaitUntilAsync(() => localReadySubmitted && localReadySignal.HasValue);

            await context.StateMachine.DidNotReceive()
                .EnterAsync<LoadGameplayState, GameLaunchConfig>(Arg.Any<GameLaunchConfig>(), Arg.Any<CancellationToken>());
        }

        [Test]
        public async System.Threading.Tasks.Task WhenBothPlayersRestartRound_ThenSessionScorePersistsAndOldSubscriptionsDoNotLeak()
        {
            using var firstSeriesService = new SeriesService();
            using var firstContext = GameplayStartupBattleshipTestFactory.CreateContext(isHost: true, seriesService: firstSeriesService);
            GameLaunchConfig? restartedConfig = null;
            var restartEnterCount = 0;
            var localReadySubmitted = false;
            RoundReadySignal? localReadySignal = null;
         
            firstContext.GameplayBridge
                .SubmitRoundReadyAsync(Arg.Any<RoundReadySignal>())
                .Returns(callInfo =>
                {
                    localReadySubmitted = true;
                    localReadySignal = callInfo.Arg<RoundReadySignal>();
                    return UniTask.CompletedTask;
                });
          
            firstContext.StateMachine
                .EnterAsync<LoadGameplayState, GameLaunchConfig>(Arg.Any<GameLaunchConfig>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    restartEnterCount++;
                    restartedConfig = callInfo.Arg<GameLaunchConfig>();
                    return UniTask.CompletedTask;
                });

            using var firstSut = firstContext.CreateSut();
            await firstSut.StartAsync(CancellationToken.None);

            firstContext.RoundFinishedEvents.OnNext(new RoundFinishedEvent(EcsGameStatus.Timeout, PlayerSlotMapping.SlotX, winLine: null));
            await UniTask.DelayFrame(1);
            firstSeriesService.Score.CurrentValue.Player1Wins.Should().Be(1);

            firstSut.HandleResultAction(ResultAction.Restart);
            await GameplayStartupBattleshipTestFactory.WaitUntilAsync(() => localReadySubmitted && localReadySignal.HasValue);
            await UniTask.DelayFrame(1);

            firstContext.IncomingReadySignals.OnNext(new RoundReadySignal(
                "guest-user",
                isReady: true,
                matchRoundId: localReadySignal!.Value.MatchRoundId,
                clientTick: 123));
          
            await GameplayStartupBattleshipTestFactory.WaitUntilAsync(() => restartEnterCount >= 1);
            await UniTask.DelayFrame(2);

            firstContext.IncomingReadySignals.OnNext(new RoundReadySignal(
                "guest-user",
                isReady: true,
                matchRoundId: localReadySignal.Value.MatchRoundId,
                clientTick: 124));
          
            await UniTask.DelayFrame(2);

            restartedConfig.Should().NotBeNull();
            restartEnterCount.Should().Be(1);

            using var secondSeriesService = new SeriesService();
          
            using var secondContext = GameplayStartupBattleshipTestFactory.CreateContext(
                isHost: true,
                launchConfig: restartedConfig,
                seriesService: secondSeriesService);
         
            using var secondSut = secondContext.CreateSut();

            await secondSut.StartAsync(CancellationToken.None);

            firstSut.Dispose();
            firstContext.IncomingReadySignals.OnNext(new RoundReadySignal("guest-user", isReady: true, matchRoundId: 1, clientTick: 125));
            await UniTask.DelayFrame(2);

            restartEnterCount.Should().Be(1);

            secondSeriesService.Score.CurrentValue.Player1Wins.Should().Be(1);
            secondSeriesService.Score.CurrentValue.Player2Wins.Should().Be(0);
            secondSeriesService.Score.CurrentValue.Draws.Should().Be(0);
        }
    }
}