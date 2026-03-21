#nullable enable

using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using Runtime.Gameplay;
using Runtime.Gameplay.Shared;
using Runtime.Games.TicTacToe.AI.Core;
using Runtime.Games.TicTacToe.AI.Profiles;
using UnityEngine.TestTools;
using EcsGameStatus = Runtime.Gameplay.Shared.EcsGameStatus;

namespace Tests.EditMode.Games.TicTacToe.AI.Turns
{
    public partial class BotTurnDriverTests
    {
        [Test]
        public void WhenDisposed_ThenIsBusyIsFalse()
        {
            var driver = CreateDriver();
            driver.IsBusy.CurrentValue.Should().BeFalse();
            driver.Dispose();
        }

        [UnityTest]
        public IEnumerator WhenCommandSequenceChangesWhileBotThinking_ThenDoesNotSubmitMove() =>
            UniTask.ToCoroutine(async () =>
            {
                var driver = CreateDriver();
                SetupEmptyBoard();
                _matchState.ActivePlayerSlotValue = 0;
                var (entered, release) = ConfigureBarrierEngine();

                await driver.StartAsync(MakeConfig(), 1, "easy", CancellationToken.None);

                _matchState.ActivePlayerSlotValue = 1;
                _matchState.CommandSequenceValue = 1;
                _matchState.CurrentPlayerChangedSubject.OnNext(new CurrentPlayerChangedEvent(1));
                await WaitUntilEngineEnteredAsync(entered);

                _matchState.CommandSequenceValue = 2;
                release.TrySetResult();
                await WaitUntilNotBusyAsync(driver);

                await _engine.Received(1).ChooseMoveAsync(Arg.Any<BotDecisionRequest>(),
                    Arg.Any<BotProfileData>(), Arg.Any<CancellationToken>());

                _matchState.SubmittedCommands.Should().BeEmpty(
                    "driver must discard move when CommandSequence changed (ADR-6)");
            
                driver.IsBusy.CurrentValue.Should().BeFalse();

                driver.Dispose();
            });

        [UnityTest]
        public IEnumerator WhenActivePlayerSlotChangesWhileBotThinking_ThenDoesNotSubmitMove() =>
            UniTask.ToCoroutine(async () =>
            {
                var driver = CreateDriver();
                SetupEmptyBoard();
                _matchState.ActivePlayerSlotValue = 0;
                var (entered, release) = ConfigureBarrierEngine();

                await driver.StartAsync(MakeConfig(), 1, "easy", CancellationToken.None);

                _matchState.ActivePlayerSlotValue = 1;
                _matchState.CommandSequenceValue = 1;
                _matchState.CurrentPlayerChangedSubject.OnNext(new CurrentPlayerChangedEvent(1));
                await WaitUntilEngineEnteredAsync(entered);

                _matchState.ActivePlayerSlotValue = 0;
                release.TrySetResult();
                await WaitUntilNotBusyAsync(driver);

                await _engine.Received(1).ChooseMoveAsync(Arg.Any<BotDecisionRequest>(),
                    Arg.Any<BotProfileData>(), Arg.Any<CancellationToken>());

                _matchState.SubmittedCommands.Should().BeEmpty(
                    "driver must discard move when ActivePlayerSlot no longer matches bot (ADR-6)");
              
                driver.IsBusy.CurrentValue.Should().BeFalse();

                driver.Dispose();
            });

        [UnityTest]
        public IEnumerator WhenRoundFinishedFiresWhileBotThinking_ThenDoesNotSubmit() =>
            UniTask.ToCoroutine(async () =>
            {
                var driver = CreateDriver();
                SetupEmptyBoard();
                _matchState.ActivePlayerSlotValue = 0;
                var (entered, release) = ConfigureBarrierEngine();

                await driver.StartAsync(MakeConfig(), 1, "easy", CancellationToken.None);

                _matchState.ActivePlayerSlotValue = 1;
                _matchState.CommandSequenceValue = 1;
                _matchState.CurrentPlayerChangedSubject.OnNext(new CurrentPlayerChangedEvent(1));
                await WaitUntilEngineEnteredAsync(entered);

                _matchState.RoundFinishedSubject.OnNext(
                    new RoundFinishedEvent(EcsGameStatus.Win, 0, null));
            
                release.TrySetResult();
                await WaitUntilNotBusyAsync(driver);

                await _engine.Received(1).ChooseMoveAsync(Arg.Any<BotDecisionRequest>(),
                    Arg.Any<BotProfileData>(), Arg.Any<CancellationToken>());

                _matchState.SubmittedCommands.Should().BeEmpty(
                    "driver must cancel and not submit when RoundFinished fires");
            
                driver.IsBusy.CurrentValue.Should().BeFalse();

                driver.Dispose();
            });

        [UnityTest]
        public IEnumerator WhenIsMatchActiveFalseWhileBotThinking_ThenDoesNotSubmit() =>
            UniTask.ToCoroutine(async () =>
            {
                var driver = CreateDriver();
                SetupEmptyBoard();
                _matchState.ActivePlayerSlotValue = 0;
                var (entered, release) = ConfigureBarrierEngine();

                await driver.StartAsync(MakeConfig(), 1, "easy", CancellationToken.None);

                _matchState.ActivePlayerSlotValue = 1;
                _matchState.CommandSequenceValue = 1;
                _matchState.CurrentPlayerChangedSubject.OnNext(new CurrentPlayerChangedEvent(1));
                await WaitUntilEngineEnteredAsync(entered);

                _matchState.IsMatchActive = false;
                release.TrySetResult();
                await WaitUntilNotBusyAsync(driver);

                await _engine.Received(1).ChooseMoveAsync(Arg.Any<BotDecisionRequest>(),
                    Arg.Any<BotProfileData>(), Arg.Any<CancellationToken>());

                _matchState.SubmittedCommands.Should().BeEmpty(
                    "driver must not submit when IsMatchActive becomes false");
               
                driver.IsBusy.CurrentValue.Should().BeFalse();

                driver.Dispose();
            });

        [UnityTest]
        public IEnumerator WhenEngineReturnsAfterCancellation_ThenDriverDoesNotSubmit() =>
            UniTask.ToCoroutine(async () =>
            {
                var driver = CreateDriver();
                SetupEmptyBoard();
                _matchState.ActivePlayerSlotValue = 0;
                var (entered, release) = ConfigureBarrierEngine(honorCancellation: false);

                await driver.StartAsync(MakeConfig(), 1, "easy", CancellationToken.None);

                _matchState.ActivePlayerSlotValue = 1;
                _matchState.CommandSequenceValue = 1;
                _matchState.CurrentPlayerChangedSubject.OnNext(new CurrentPlayerChangedEvent(1));
                await WaitUntilEngineEnteredAsync(entered);

                _matchState.RoundFinishedSubject.OnNext(
                    new RoundFinishedEvent(EcsGameStatus.Draw, null, null));
               
                release.TrySetResult();
                await WaitUntilNotBusyAsync(driver);

                await _engine.Received(1).ChooseMoveAsync(Arg.Any<BotDecisionRequest>(),
                    Arg.Any<BotProfileData>(), Arg.Any<CancellationToken>());

                _matchState.SubmittedCommands.Should().BeEmpty(
                    "driver must not submit even if engine returns after cancellation");

                driver.Dispose();
            });

        [UnityTest]
        public IEnumerator WhenStartAsyncAndItsAlreadyBotTurn_ThenSubmitsMoveImmediately() =>
            UniTask.ToCoroutine(async () =>
            {
                var driver = CreateDriver();
                SetupEmptyBoard();
                _matchState.ActivePlayerSlotValue = 1;
                _matchState.CommandSequenceValue = 0;

                var expectedMove = new CellId(1, 1);
               
                _engine.ChooseMoveAsync(Arg.Any<BotDecisionRequest>(), Arg.Any<BotProfileData>(),
                        Arg.Any<CancellationToken>())
                    .Returns(UniTask.FromResult(expectedMove));

                await driver.StartAsync(MakeConfig(), 1, "easy", CancellationToken.None);
                await WaitForSubmitAsync();

                _matchState.SubmittedCommands.Should().ContainSingle();
                var submitted = _matchState.SubmittedCommands[0];
                submitted.Should().BeAssignableTo<MakeMoveCommand>();
                ((MakeMoveCommand)submitted).CellId.Should().Be(expectedMove);

                driver.Dispose();
            });

        [UnityTest]
        public IEnumerator WhenAllRetriesExhausted_ThenIsDisabledTrueAndIsBusyFalse() =>
            UniTask.ToCoroutine(async () =>
            {
                var driver = CreateDriver();
                SetupEmptyBoard();
                _matchState.ActivePlayerSlotValue = 0;

                var previousIgnore = LogAssert.ignoreFailingMessages;
                LogAssert.ignoreFailingMessages = true;

                _engine.ChooseMoveAsync(Arg.Any<BotDecisionRequest>(), Arg.Any<BotProfileData>(),
                        Arg.Any<CancellationToken>())
                    .Returns(UniTask.FromResult(new CellId(1, 1)));

                _matchState.AcceptCommands = false;

                await driver.StartAsync(MakeConfig(), 1, "easy", CancellationToken.None);

                _matchState.ActivePlayerSlotValue = 1;
                _matchState.CurrentPlayerChangedSubject.OnNext(new CurrentPlayerChangedEvent(1));

                try
                {
                    await WaitForSubmitAsync(minCount: 3);

                    driver.IsDisabled.CurrentValue.Should().BeTrue("bot should be disabled after all retries exhausted");
                    driver.IsBusy.CurrentValue.Should().BeFalse("IsBusy must be reset after exhausted retries");

                    _matchState.SubmittedCommands.Clear();
                    _matchState.CurrentPlayerChangedSubject.OnNext(new CurrentPlayerChangedEvent(1));
                    await UniTask.DelayFrame(5);
                    
                    _matchState.SubmittedCommands.Should().BeEmpty(
                        "disabled bot should ignore subsequent CurrentPlayerChanged events");
                }
                finally
                {
                    LogAssert.ignoreFailingMessages = previousIgnore;
                    driver.Dispose();
                }
            });
    }
}