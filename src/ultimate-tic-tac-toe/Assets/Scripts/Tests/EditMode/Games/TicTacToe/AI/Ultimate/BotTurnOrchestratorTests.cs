#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using R3;
using Runtime.Gameplay;
using Runtime.Gameplay.ECS;
using Runtime.Gameplay.Shared;
using Runtime.Games.TicTacToe.AI.Ultimate;
using Runtime.Games.TicTacToe.AI.Ultimate.Core;
using Runtime.Games.TicTacToe.AI.Ultimate.Execution;
using Runtime.Games.TicTacToe.Moves;
using Runtime.Games.TicTacToe.Rules;
using Runtime.Games.TicTacToe.Ultimate.Rules;
using EcsGameStatus = Runtime.Gameplay.Shared.EcsGameStatus;

namespace Tests.EditMode.Games.TicTacToe.AI.Ultimate
{
    [TestFixture]
    [Category("Unit")]
    public class BotTurnOrchestratorTests
    {
        [Test]
        public void WhenStartCalledTwice_ThenThrowsInvalidOperationException()
        {
            var sut = CreateSut();

            sut.StartAsync(0, "easy", CancellationToken.None).AsTask().GetAwaiter().GetResult();
            Action act = () => sut.StartAsync(0, "easy", CancellationToken.None).AsTask().GetAwaiter().GetResult();

            act.Should().Throw<InvalidOperationException>();
            sut.Dispose();
        }

        [Test]
        public void WhenStartAfterDispose_ThenThrowsObjectDisposedException()
        {
            var sut = CreateSut();
            sut.Dispose();

            Action act = () => sut.StartAsync(0, "easy", CancellationToken.None).AsTask().GetAwaiter().GetResult();

            act.Should().Throw<ObjectDisposedException>();
        }

        [Test]
        public void WhenStopCalledMultipleTimes_ThenIsIdempotent()
        {
            var sut = CreateSut();
            sut.StartAsync(0, "easy", CancellationToken.None).AsTask().GetAwaiter().GetResult();

            sut.Stop();
            sut.Stop();

            sut.State.Should().Be(BotOrchestratorState.Stopped);
            sut.Dispose();
        }

        [Test]
        public void WhenFailSafeEnteredTwice_ThenSecondAttemptReturnsFalse()
        {
            var gateway = new LocalMatchFailSafeGateway();

            LogAssert.ignoreFailingMessages = true;
            try
            {
                var first = gateway.TryEnterAbortState("Errors.Bot.NoLegalMoves");
                var second = gateway.TryEnterAbortState("Errors.Bot.NoLegalMoves");

                first.Should().BeTrue();
                second.Should().BeFalse();
                gateway.IsInputLocked.Should().BeTrue();
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }
        }

        [Test]
        public async System.Threading.Tasks.Task WhenCancelDuringRetrySubmit_ThenDoesNotEnterFailSafe()
        {
            var events = new FakeEvents();
            var snapshot = new FakeSnapshot { ActivePlayerSlot = 1 };
            var profiles = new FakeProfiles();
            var stateReader = new AlwaysRequestStateReader();
            var engine = new RetryAwareEngine();
            var rngFactory = new BotRngSessionFactory();
            var failSafe = new SpyFailSafeGateway();

            BotTurnOrchestrator? sut = null;
            var sink = new RetrySink(() => sut!.Stop());
            sut = new BotTurnOrchestrator(events, snapshot, profiles, stateReader, engine, rngFactory, sink, failSafe);

            await sut.StartAsync(0, "easy", CancellationToken.None);
            snapshot.ActivePlayerSlot = 0;

            Func<System.Threading.Tasks.Task> act = async () => await sut.TriggerIfBotTurnAsync(CancellationToken.None);

            await act.Should().NotThrowAsync();
            failSafe.AbortCount.Should().Be(0);
            sut.Dispose();
        }

        [Test]
        public async System.Threading.Tasks.Task WhenFailSafeTransitionRejected_ThenDoesNotPublishMoveFailed()
        {
            var events = new FakeEvents();
            var snapshot = new FakeSnapshot { ActivePlayerSlot = 1 };
            var profiles = new FakeProfiles();
            var stateReader = new NoLegalMovesStateReader();
            var engine = new FakeEngine();
            var rngFactory = new BotRngSessionFactory();
            var sink = new FakeSink();
            var failSafe = new RejectingFailSafeGateway();
            var sut = new BotTurnOrchestrator(events, snapshot, profiles, stateReader, engine, rngFactory, sink, failSafe);

            var failedEvents = 0;
            var subscription = sut.MoveFailed.Subscribe(_ => failedEvents++);

            await sut.StartAsync(0, "easy", CancellationToken.None);
            snapshot.ActivePlayerSlot = 0;

            await sut.TriggerIfBotTurnAsync(CancellationToken.None);

            failSafe.Calls.Should().Be(1);
            failedEvents.Should().Be(0);

            subscription.Dispose();
            sut.Dispose();
        }

        [Test]
        public async System.Threading.Tasks.Task WhenNoLegalMovesFailSafeEntered_ThenCancelsTurnBeforeMoveFailedEvent()
        {
            var events = new FakeEvents();
            var snapshot = new FakeSnapshot { ActivePlayerSlot = 1 };
            var profiles = new FakeProfiles();
            var stateReader = new NoLegalMovesStateReader();
            var engine = new FakeEngine();
            var rngFactory = new BotRngSessionFactory();
            var sink = new TrackingSink();
            var failSafe = new SpyFailSafeGateway();
            var sut = new BotTurnOrchestrator(events, snapshot, profiles, stateReader, engine, rngFactory, sink, failSafe);

            var failedEvents = 0;
            var thinkingAtPublish = true;
            BotTurnId? inFlightAtPublish = default;
            var subscription = sut.MoveFailed.Subscribe(evt =>
            {
                failedEvents++;
                thinkingAtPublish = sut.IsThinking.CurrentValue;
                inFlightAtPublish = sut.InFlightTurnId.CurrentValue;
            });

            await sut.StartAsync(0, "easy", CancellationToken.None);
            snapshot.ActivePlayerSlot = 0;

            await sut.TriggerIfBotTurnAsync(CancellationToken.None);

            failSafe.AbortCount.Should().Be(1);
            failedEvents.Should().Be(1);
            thinkingAtPublish.Should().BeFalse();
            inFlightAtPublish.Should().BeNull();

            subscription.Dispose();
            sut.Dispose();
        }

        [Test]
        public async System.Threading.Tasks.Task WhenFailSafeTransitionRejected_ThenDisablesFurtherTurnAttempts()
        {
            var events = new FakeEvents();
            var snapshot = new FakeSnapshot { ActivePlayerSlot = 1 };
            var profiles = new FakeProfiles();
            var stateReader = new CountingNoLegalMovesStateReader();
            var engine = new FakeEngine();
            var rngFactory = new BotRngSessionFactory();
            var sink = new FakeSink();
            var failSafe = new RejectingFailSafeGateway();
            var sut = new BotTurnOrchestrator(events, snapshot, profiles, stateReader, engine, rngFactory, sink, failSafe);

            await sut.StartAsync(0, "easy", CancellationToken.None);
            snapshot.ActivePlayerSlot = 0;

            await sut.TriggerIfBotTurnAsync(CancellationToken.None);
            await sut.TriggerIfBotTurnAsync(CancellationToken.None);

            stateReader.Calls.Should().Be(1);
            failSafe.Calls.Should().Be(1);
            sut.Dispose();
        }

        [Test]
        public void WhenStartAsyncWithUnknownDifficultyId_ThenThrowsInvalidOperationException()
        {
            var events = new FakeEvents();
            var snapshot = new FakeSnapshot();
            var stateReader = new FakeStateReader();
            var engine = new FakeEngine();
            var rngFactory = new BotRngSessionFactory();
            var sink = new FakeSink();
            var failSafe = new LocalMatchFailSafeGateway();
            var profiles = new MissingProfiles();
            var sut = new BotTurnOrchestrator(events, snapshot, profiles, stateReader, engine, rngFactory, sink, failSafe);

            Action act = () => sut.StartAsync(0, "unknown", CancellationToken.None).AsTask().GetAwaiter().GetResult();

            act.Should().Throw<InvalidOperationException>();
            sut.State.Should().Be(BotOrchestratorState.NotStarted);
            sut.Dispose();
        }

        [TestCase(null)]
        [TestCase("")]
        public void WhenStartAsyncCalledWithNullOrEmptyDifficultyId_ThenThrowsInvalidOperationException(string difficultyId)
        {
            var events = new FakeEvents();
            var snapshot = new FakeSnapshot();
            var stateReader = new FakeStateReader();
            var engine = new FakeEngine();
            var rngFactory = new BotRngSessionFactory();
            var sink = new FakeSink();
            var failSafe = new LocalMatchFailSafeGateway();
            var profiles = new MissingProfiles();
            var sut = new BotTurnOrchestrator(events, snapshot, profiles, stateReader, engine, rngFactory, sink, failSafe);

            Action act = () => sut.StartAsync(0, difficultyId!, CancellationToken.None).AsTask().GetAwaiter().GetResult();

            act.Should().Throw<InvalidOperationException>();
            sut.State.Should().Be(BotOrchestratorState.NotStarted);
            sut.Dispose();
        }

        [Test]
        public void WhenStartAsyncWithCancelledToken_ThenStartCompletesAndNoSubscriptionsRemainAfterDispose()
        {
            var events = new FakeEvents();
            var snapshot = new FakeSnapshot { ActivePlayerSlot = 1 };
            var profiles = new FakeProfiles();
            var stateReader = new CountingStateReader();
            var engine = new FakeEngine();
            var rngFactory = new BotRngSessionFactory();
            var sink = new TrackingSink();
            var failSafe = new LocalMatchFailSafeGateway();
            var sut = new BotTurnOrchestrator(events, snapshot, profiles, stateReader, engine, rngFactory, sink, failSafe);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            sut.StartAsync(0, "easy", cts.Token).AsTask().GetAwaiter().GetResult();
            sut.State.Should().Be(BotOrchestratorState.Active);
            sut.IsStarted.CurrentValue.Should().BeTrue();
            sut.InFlightTurnId.CurrentValue.Should().BeNull();
            sut.LastSubmittedTurnId.CurrentValue.Should().BeNull();

            sut.Dispose();

            snapshot.ActivePlayerSlot = 0;
            Action act = () =>
            {
                events.EmitCurrentPlayerChanged(0);
                events.EmitCurrentPlayerChanged(0);
                events.EmitRoundFinished();
            };

            act.Should().NotThrow();
            stateReader.Calls.Should().Be(0);
            sink.SubmitCount.Should().Be(0);
        }

        [Test]
        public void WhenDisposeCalledTwice_ThenDoesNotThrowAndStateIsDisposed()
        {
            var sut = CreateSut();
            sut.StartAsync(0, "easy", CancellationToken.None).AsTask().GetAwaiter().GetResult();

            Action act = () =>
            {
                sut.Dispose();
                sut.Dispose();
            };

            act.Should().NotThrow();
            sut.State.Should().Be(BotOrchestratorState.Disposed);
        }

        [Test]
        public async System.Threading.Tasks.Task WhenDisposeCalledDuringInFlightTurn_ThenCancelsTurnAndResetsThinking()
        {
            var events = new FakeEvents();
            var snapshot = new FakeSnapshot { ActivePlayerSlot = 1 };
            var profiles = new FakeProfiles();
            var stateReader = new AlwaysRequestStateReader();
            var engine = new BarrierEngine();
            var rngFactory = new BotRngSessionFactory();
            var sink = new TrackingSink();
            var failSafe = new SpyFailSafeGateway();
            var sut = new BotTurnOrchestrator(events, snapshot, profiles, stateReader, engine, rngFactory, sink, failSafe);

            await sut.StartAsync(0, "easy", CancellationToken.None);
            snapshot.ActivePlayerSlot = 0;
            var trigger = sut.TriggerIfBotTurnAsync(CancellationToken.None);
            await engine.WaitUntilEnteredAsync();

            sut.Dispose();
            await trigger;

            sut.State.Should().Be(BotOrchestratorState.Disposed);
            sut.IsThinking.CurrentValue.Should().BeFalse();
            sut.InFlightTurnId.CurrentValue.Should().BeNull();
            sink.SubmitCount.Should().Be(0);
        }

        [Test]
        public async System.Threading.Tasks.Task WhenStopCalledDuringInFlightTurn_ThenCancelsTurnAndClearsInFlightWithoutSubmit()
        {
            var events = new FakeEvents();
            var snapshot = new FakeSnapshot { ActivePlayerSlot = 1 };
            var profiles = new FakeProfiles();
            var stateReader = new AlwaysRequestStateReader();
            var engine = new BarrierEngine();
            var rngFactory = new BotRngSessionFactory();
            var sink = new TrackingSink();
            var failSafe = new SpyFailSafeGateway();
            var sut = new BotTurnOrchestrator(events, snapshot, profiles, stateReader, engine, rngFactory, sink, failSafe);

            await sut.StartAsync(0, "easy", CancellationToken.None);
            snapshot.ActivePlayerSlot = 0;
            var trigger = sut.TriggerIfBotTurnAsync(CancellationToken.None);
            await engine.WaitUntilEnteredAsync();

            sut.Stop();
            await trigger;

            sink.SubmitCount.Should().Be(0);
            sut.IsThinking.CurrentValue.Should().BeFalse();
            sut.InFlightTurnId.CurrentValue.Should().BeNull();
            sut.State.Should().Be(BotOrchestratorState.Stopped);
            sut.Dispose();
        }

        [Test]
        public async System.Threading.Tasks.Task WhenRoundFinishedRaised_ThenActiveTurnIsCancelledWithoutSubmit()
        {
            var events = new FakeEvents();
            var snapshot = new FakeSnapshot { ActivePlayerSlot = 1 };
            var profiles = new FakeProfiles();
            var stateReader = new AlwaysRequestStateReader();
            var engine = new BarrierEngine();
            var rngFactory = new BotRngSessionFactory();
            var sink = new TrackingSink();
            var failSafe = new SpyFailSafeGateway();
            var sut = new BotTurnOrchestrator(events, snapshot, profiles, stateReader, engine, rngFactory, sink, failSafe);

            await sut.StartAsync(0, "easy", CancellationToken.None);
            snapshot.ActivePlayerSlot = 0;
            var trigger = sut.TriggerIfBotTurnAsync(CancellationToken.None);
            await engine.WaitUntilEnteredAsync();

            events.EmitRoundFinished();
            await trigger;

            sink.SubmitCount.Should().Be(0);
            sut.IsThinking.CurrentValue.Should().BeFalse();
            sut.InFlightTurnId.CurrentValue.Should().BeNull();
            sut.Dispose();
        }

        [Test]
        public async System.Threading.Tasks.Task WhenCurrentPlayerChangedDuplicateTurn_ThenPublishesDuplicateIgnoredAndNoSecondSubmit()
        {
            var events = new FakeEvents();
            var snapshot = new FakeSnapshot { ActivePlayerSlot = 1, CommandSequence = 10 };
            var profiles = new FakeProfiles();
            var stateReader = new AlwaysRequestStateReader();
            var engine = new BarrierEngine();
            var rngFactory = new BotRngSessionFactory();
            var sink = new TrackingSink();
            var failSafe = new SpyFailSafeGateway();
            var sut = new BotTurnOrchestrator(events, snapshot, profiles, stateReader, engine, rngFactory, sink, failSafe);

            var duplicateCount = 0;
            using var sub = sut.DuplicateIgnored.Subscribe(_ => duplicateCount++);

            await sut.StartAsync(0, "easy", CancellationToken.None);
            snapshot.ActivePlayerSlot = 0;

            events.EmitCurrentPlayerChanged(0);
            await engine.WaitUntilEnteredAsync();
            events.EmitCurrentPlayerChanged(0);

            engine.Release();
            await WaitUntilAsync(() => sink.SubmitCount == 1);

            duplicateCount.Should().Be(1);
            sink.SubmitCount.Should().Be(1);
            sut.Dispose();
        }

        [Test]
        public async System.Threading.Tasks.Task WhenTriggerIfBotTurnCalledWhileStoppedOrNotStarted_ThenReturnsWithoutSideEffects()
        {
            var events = new FakeEvents();
            var snapshot = new FakeSnapshot { ActivePlayerSlot = 1 };
            var profiles = new FakeProfiles();
            var stateReader = new CountingStateReader();
            var engine = new FakeEngine();
            var rngFactory = new BotRngSessionFactory();
            var sink = new TrackingSink();
            var failSafe = new SpyFailSafeGateway();
            var sut = new BotTurnOrchestrator(events, snapshot, profiles, stateReader, engine, rngFactory, sink, failSafe);

            await sut.TriggerIfBotTurnAsync(CancellationToken.None);
            await sut.StartAsync(0, "easy", CancellationToken.None);
            sut.Stop();
            await sut.TriggerIfBotTurnAsync(CancellationToken.None);

            stateReader.Calls.Should().Be(0);
            sink.SubmitCount.Should().Be(0);
            sut.Dispose();
        }

        [Test]
        public async System.Threading.Tasks.Task WhenCurrentPlayerChangedAfterDispose_ThenDoesNotInvokeTriggerPath()
        {
            var events = new FakeEvents();
            var snapshot = new FakeSnapshot { ActivePlayerSlot = 1 };
            var profiles = new FakeProfiles();
            var stateReader = new CountingStateReader();
            var engine = new FakeEngine();
            var rngFactory = new BotRngSessionFactory();
            var sink = new TrackingSink();
            var failSafe = new SpyFailSafeGateway();
            var sut = new BotTurnOrchestrator(events, snapshot, profiles, stateReader, engine, rngFactory, sink, failSafe);

            await sut.StartAsync(0, "easy", CancellationToken.None);
            sut.Dispose();

            snapshot.ActivePlayerSlot = 0;
            events.EmitCurrentPlayerChanged(0);

            stateReader.Calls.Should().Be(0);
            sink.SubmitCount.Should().Be(0);
        }

        [Test]
        public async System.Threading.Tasks.Task WhenRetryAttempt1RejectedAndAttempt2Accepted_ThenSubmitsExactlyOnceWithRetryTurnId()
        {
            var events = new FakeEvents();
            var snapshot = new FakeSnapshot { ActivePlayerSlot = 1, CommandSequence = 20 };
            var profiles = new FakeProfiles();
            var stateReader = new AlwaysRequestStateReader();
            var engine = new FakeEngine(new CellId(0, 0));
            var rngFactory = new BotRngSessionFactory();
            var sink = new TrackingSink(rejectBeforeAccept: 1, onReject: () => snapshot.CommandSequence++);
            var failSafe = new SpyFailSafeGateway();
            var sut = new BotTurnOrchestrator(events, snapshot, profiles, stateReader, engine, rngFactory, sink, failSafe);

            await sut.StartAsync(0, "easy", CancellationToken.None);
            snapshot.ActivePlayerSlot = 0;

            await sut.TriggerIfBotTurnAsync(CancellationToken.None);

            sink.SubmitCount.Should().Be(2);
            sink.AcceptedCount.Should().Be(1);
            sut.LastSubmittedTurnId.CurrentValue.Should().Be(BotTurnId.Build(21, 0));
            failSafe.AbortCount.Should().Be(0);
            sut.Dispose();
        }

        [Test]
        public async System.Threading.Tasks.Task WhenRetryPolicyExhausted_ThenRaisesEngineErrorAndDisablesFurtherTurns()
        {
            var events = new FakeEvents();
            var snapshot = new FakeSnapshot { ActivePlayerSlot = 1, CommandSequence = 30 };
            var profiles = new FakeProfiles();
            var stateReader = new AlwaysRequestStateReader();
            var engine = new FakeEngine(new CellId(0, 0));
            var rngFactory = new BotRngSessionFactory();
            var sink = new TrackingSink(rejectBeforeAccept: int.MaxValue, onReject: () => snapshot.CommandSequence++);
            var failSafe = new SpyFailSafeGateway();
            var sut = new BotTurnOrchestrator(events, snapshot, profiles, stateReader, engine, rngFactory, sink, failSafe);

            var failedCount = 0;
            using var failedSub = sut.MoveFailed.Subscribe(_ => failedCount++);

            await sut.StartAsync(0, "easy", CancellationToken.None);
            snapshot.ActivePlayerSlot = 0;

            await sut.TriggerIfBotTurnAsync(CancellationToken.None);
            var submitAfterFirstTrigger = sink.SubmitCount;

            await sut.TriggerIfBotTurnAsync(CancellationToken.None);

            failSafe.AbortCount.Should().Be(1);
            failedCount.Should().Be(1);
            sink.SubmitCount.Should().Be(submitAfterFirstTrigger);
            sut.Dispose();
        }

        [Test]
        public async System.Threading.Tasks.Task WhenRetryAttempt2RejectedAndFallbackAccepted_ThenUsesDeterministicFirstLegalMove()
        {
            var events = new FakeEvents();
            var snapshot = new FakeSnapshot { ActivePlayerSlot = 1, CommandSequence = 40 };
            var profiles = new FakeProfiles();
            var stateReader = new AlwaysRequestStateReader();
            var engine = new FakeEngine(new CellId(0, 1));
            var rngFactory = new BotRngSessionFactory();
            var sink = new FallbackAssertingSink(new CellId(0, 0), () => snapshot.CommandSequence++);
            var failSafe = new SpyFailSafeGateway();
            var sut = new BotTurnOrchestrator(events, snapshot, profiles, stateReader, engine, rngFactory, sink, failSafe);

            await sut.StartAsync(0, "easy", CancellationToken.None);
            snapshot.ActivePlayerSlot = 0;

            await sut.TriggerIfBotTurnAsync(CancellationToken.None);

            sink.SubmitCount.Should().Be(3);
            sink.AcceptedMove.Should().Be(new CellId(0, 0));
            failSafe.AbortCount.Should().Be(0);
            sut.Dispose();
        }

        [Test]
        public async System.Threading.Tasks.Task WhenNoLegalMovesInProgress_ThenPublishesMoveFailedOnlyAfterGateLocked()
        {
            var events = new FakeEvents();
            var snapshot = new FakeSnapshot { ActivePlayerSlot = 1 };
            var profiles = new FakeProfiles();
            var stateReader = new NoLegalMovesStateReader();
            var engine = new FakeEngine();
            var rngFactory = new BotRngSessionFactory();
            var sink = new TrackingSink();
            var failSafe = new SpyFailSafeGateway();
            var sut = new BotTurnOrchestrator(events, snapshot, profiles, stateReader, engine, rngFactory, sink, failSafe);

            var lockedAtEvent = false;
            BotTurnId? inFlightAtEvent = BotTurnId.Build(0, 0);
            using var sub = sut.MoveFailed.Subscribe(_ =>
            {
                lockedAtEvent = failSafe.IsInputLocked;
                inFlightAtEvent = sut.InFlightTurnId.CurrentValue;
            });

            await sut.StartAsync(0, "easy", CancellationToken.None);
            snapshot.ActivePlayerSlot = 0;

            await sut.TriggerIfBotTurnAsync(CancellationToken.None);

            failSafe.AbortCount.Should().Be(1);
            lockedAtEvent.Should().BeTrue();
            inFlightAtEvent.Should().BeNull();
            sink.SubmitCount.Should().Be(0);
            sut.Dispose();
        }

        [Test]
        public async System.Threading.Tasks.Task WhenPreMoveDelayActiveAndCancellationRequested_ThenNoSubmitOccurs()
        {
            var events = new FakeEvents();
            var snapshot = new FakeSnapshot { ActivePlayerSlot = 1 };
            var profiles = new FakeProfiles(preMoveDelayMs: 200);
            var stateReader = new AlwaysRequestStateReader();
            var engine = new CountingEngine();
            var rngFactory = new BotRngSessionFactory();
            var sink = new TrackingSink();
            var failSafe = new SpyFailSafeGateway();
            var sut = new BotTurnOrchestrator(events, snapshot, profiles, stateReader, engine, rngFactory, sink, failSafe);
            using var cts = new CancellationTokenSource();

            await sut.StartAsync(0, "easy", CancellationToken.None);
            snapshot.ActivePlayerSlot = 0;

            var triggerTask = sut.TriggerIfBotTurnAsync(cts.Token);
            await WaitUntilAsync(() => sut.IsThinking.CurrentValue && sut.InFlightTurnId.CurrentValue.HasValue);
            await UniTask.Delay(20);
            cts.Cancel();
            await triggerTask;

            engine.Calls.Should().Be(0);
            sink.SubmitCount.Should().Be(0);
            sut.IsThinking.CurrentValue.Should().BeFalse();
            sut.Dispose();
        }

        [Test]
        public async System.Threading.Tasks.Task WhenCancellationBeforeSubmitWindow_ThenNoCommandIsSubmitted()
        {
            var events = new FakeEvents();
            var snapshot = new FakeSnapshot { ActivePlayerSlot = 1 };
            var profiles = new FakeProfiles();
            var stateReader = new AlwaysRequestStateReader();
            var engine = new BarrierEngine();
            var rngFactory = new BotRngSessionFactory();
            var sink = new TrackingSink();
            var failSafe = new SpyFailSafeGateway();
            var sut = new BotTurnOrchestrator(events, snapshot, profiles, stateReader, engine, rngFactory, sink, failSafe);
            using var cts = new CancellationTokenSource();

            await sut.StartAsync(0, "easy", CancellationToken.None);
            snapshot.ActivePlayerSlot = 0;

            var trigger = sut.TriggerIfBotTurnAsync(cts.Token);
            await engine.WaitUntilEnteredAsync();
            cts.Cancel();
            engine.Release();
            await trigger;

            sink.SubmitCount.Should().Be(0);
            sut.IsThinking.CurrentValue.Should().BeFalse();
            sut.InFlightTurnId.CurrentValue.Should().BeNull();
            sut.Dispose();
        }

        [Test]
        public async System.Threading.Tasks.Task WhenRestartStorm100Iterations_ThenNoGrowingActiveSubscriptionsOrUnhandledErrors()
        {
            var totalStateReaderCalls = 0;
            var exceptions = new List<Exception>();

            for (var i = 0; i < 100; i++)
            {
                var events = new FakeEvents();
                var snapshot = new FakeSnapshot { ActivePlayerSlot = 1, CommandSequence = i + 1 };
                var profiles = new FakeProfiles();
                var stateReader = new CountingStateReader();
                var engine = new FakeEngine(new CellId(0, 0));
                var rngFactory = new BotRngSessionFactory();
                var sink = new TrackingSink();
                var failSafe = new SpyFailSafeGateway();
                var sut = new BotTurnOrchestrator(events, snapshot, profiles, stateReader, engine, rngFactory, sink, failSafe);

                await sut.StartAsync(0, "easy", CancellationToken.None);
                snapshot.ActivePlayerSlot = 0;
                await sut.TriggerIfBotTurnAsync(CancellationToken.None);

                sut.Dispose();

                try
                {
                    events.EmitCurrentPlayerChanged(0);
                    events.EmitCurrentPlayerChanged(0);
                    events.EmitRoundFinished();
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }

                stateReader.Calls.Should().Be(1);
                totalStateReaderCalls += stateReader.Calls;
            }

            totalStateReaderCalls.Should().Be(100);
            exceptions.Should().BeEmpty();
        }

        [Test]
        public async System.Threading.Tasks.Task WhenDiagnosticsEnabled_ThenPublishesDepthIterationsAndDegradationReason()
        {
            var events = new FakeEvents();
            var snapshot = new FakeSnapshot { ActivePlayerSlot = 1 };
            var profiles = new FakeProfiles(enableDiagnostics: true);
            var stateReader = new AlwaysRequestStateReader();
            var engine = new FakeEngine(
                move: new CellId(0, 0),
                degradationReason: BotFailureReason.TimeoutBest,
                searchDepthReached: 3,
                iterationsCompleted: 2);
            var rngFactory = new BotRngSessionFactory();
            var sink = new TrackingSink();
            var failSafe = new SpyFailSafeGateway();
            var sut = new BotTurnOrchestrator(events, snapshot, profiles, stateReader, engine, rngFactory, sink, failSafe);

            BotDecisionDiagnostics? diagnostics = null;
            using var sub = sut.Diagnostics.Subscribe(evt => diagnostics = evt);

            await sut.StartAsync(0, "easy", CancellationToken.None);
            snapshot.ActivePlayerSlot = 0;

            await sut.TriggerIfBotTurnAsync(CancellationToken.None);

            diagnostics.Should().NotBeNull();
            diagnostics!.Value.DepthReached.Should().Be(3);
            diagnostics.Value.IterationCount.Should().Be(2);
            diagnostics.Value.DegradationReason.Should().Be(BotFailureReason.TimeoutBest);
            sut.Dispose();
        }

        [Test]
        public async System.Threading.Tasks.Task WhenDiagnosticsDisabled_ThenDoesNotPublishDiagnosticsPayload()
        {
            var events = new FakeEvents();
            var snapshot = new FakeSnapshot { ActivePlayerSlot = 1 };
            var profiles = new FakeProfiles(enableDiagnostics: false);
            var stateReader = new AlwaysRequestStateReader();
            var engine = new FakeEngine(new CellId(0, 0));
            var rngFactory = new BotRngSessionFactory();
            var sink = new TrackingSink();
            var failSafe = new SpyFailSafeGateway();
            var sut = new BotTurnOrchestrator(events, snapshot, profiles, stateReader, engine, rngFactory, sink, failSafe);

            var diagnosticsCount = 0;
            using var sub = sut.Diagnostics.Subscribe(_ => diagnosticsCount++);

            await sut.StartAsync(0, "easy", CancellationToken.None);
            snapshot.ActivePlayerSlot = 0;

            await sut.TriggerIfBotTurnAsync(CancellationToken.None);

            diagnosticsCount.Should().Be(0);
            sut.Dispose();
        }

        private static async UniTask WaitUntilAsync(Func<bool> condition)
        {
            for (var i = 0; i < 120; i++)
            {
                if (condition())
                {
                    return;
                }

                await UniTask.DelayFrame(1);
            }

            condition().Should().BeTrue();
        }

        private static BotTurnOrchestrator CreateSut()
        {
            var events = new FakeEvents();
            var snapshot = new FakeSnapshot();
            var profiles = new FakeProfiles();
            var stateReader = new FakeStateReader();
            var engine = new FakeEngine();
            var rngFactory = new BotRngSessionFactory();
            var sink = new FakeSink();
            var failSafe = new LocalMatchFailSafeGateway();

            return new BotTurnOrchestrator(events, snapshot, profiles, stateReader, engine, rngFactory, sink, failSafe);
        }

        private sealed class FakeEvents : IGameplayEventStream
        {
            private readonly Subject<CellChangedEvent> _cell = new();
            private readonly Subject<LastMoveChangedEvent> _last = new();
            private readonly Subject<CurrentPlayerChangedEvent> _player = new();
            private readonly Subject<CommandRejectedEvent> _reject = new();
            private readonly Subject<RoundFinishedEvent> _finished = new();

            public Observable<CellChangedEvent> CellChanged => _cell;
            public Observable<LastMoveChangedEvent> LastMoveChanged => _last;
            public Observable<CurrentPlayerChangedEvent> CurrentPlayerChanged => _player;
            public Observable<CommandRejectedEvent> CommandRejected => _reject;
            public Observable<RoundFinishedEvent> RoundFinished => _finished;

            public void EmitCurrentPlayerChanged(int slot)
            {
                _player.OnNext(new CurrentPlayerChangedEvent(slot));
            }

            public void EmitRoundFinished()
            {
                _finished.OnNext(new RoundFinishedEvent(EcsGameStatus.Draw, null, null));
            }
        }

        private sealed class FakeSnapshot : Runtime.Gameplay.IGameplaySnapshotProvider
        {
            public int GetCellSlot(CellId cellId) => -1;
            public IReadOnlyList<CellSnapshot> GetAllCells() => Array.Empty<CellSnapshot>();
            public long CommandSequence { get; set; } = 10;
            public int ActivePlayerSlot { get; set; } = 0;
            public CellId? LastMove => null;
        }

        private sealed class FakeProfiles : IUltimateBotProfileCatalog
        {
            private readonly int _preMoveDelayMs;
            private readonly bool _enableDiagnostics;

            public FakeProfiles(int preMoveDelayMs = 0, bool enableDiagnostics = false)
            {
                _preMoveDelayMs = preMoveDelayMs;
                _enableDiagnostics = enableDiagnostics;
            }

            public bool TryGet(string difficultyId, out UltimateBotDifficultyProfileData profile)
            {
                profile = new UltimateBotDifficultyProfileData(
                    "easy",
                    "1.0.0",
                    new string('a', 64),
                    10,
                    1,
                    1,
                    100,
                    1,
                    0f,
                    1f,
                    1f,
                    1f,
                    1f,
                    true,
                    1,
                    _preMoveDelayMs,
                    _enableDiagnostics,
                    EvaluationWeights.Default);
                return true;
            }
        }

        private sealed class MissingProfiles : IUltimateBotProfileCatalog
        {
            public bool TryGet(string difficultyId, out UltimateBotDifficultyProfileData profile)
            {
                profile = default;
                return false;
            }
        }

        private sealed class CountingStateReader : IUltimateBotStateReader
        {
            public int Calls { get; private set; }

            public bool TryBuildDecisionRequest(int botSlot, BotTurnId turnId, UltimateBotDifficultyProfileData profile, IBotRngSession rng,
                out UltimateBotDecisionRequest request, out BotFailureReason? failReason)
            {
                Calls++;
                request = default;
                failReason = null;
                return false;
            }
        }

        private sealed class FakeStateReader : IUltimateBotStateReader
        {
            public bool TryBuildDecisionRequest(
                int botSlot,
                BotTurnId turnId,
                UltimateBotDifficultyProfileData profile,
                IBotRngSession rng,
                out UltimateBotDecisionRequest request,
                out BotFailureReason? failReason)
            {
                request = default;
                failReason = null;
                return false;
            }
        }

        private sealed class AlwaysRequestStateReader : IUltimateBotStateReader
        {
            public bool TryBuildDecisionRequest(
                int botSlot,
                BotTurnId turnId,
                UltimateBotDifficultyProfileData profile,
                IBotRngSession rng,
                out UltimateBotDecisionRequest request,
                out BotFailureReason? failReason)
            {
                var cells = new PlayerMark[81];
                var miniBoards = new MiniBoardStatus[9];
                for (var i = 0; i < miniBoards.Length; i++)
                {
                    miniBoards[i] = MiniBoardStatus.InProgress;
                }

                request = new UltimateBotDecisionRequest(
                    turnId,
                    new UltimateBoardSnapshot(cells, miniBoards, AllowedMajors.All, 0),
                    new[] { new CellId(0, 0), new CellId(0, 1) },
                    profile,
                    rng);
                failReason = null;
                return true;
            }
        }

        private sealed class NoLegalMovesStateReader : IUltimateBotStateReader
        {
            public bool TryBuildDecisionRequest(
                int botSlot,
                BotTurnId turnId,
                UltimateBotDifficultyProfileData profile,
                IBotRngSession rng,
                out UltimateBotDecisionRequest request,
                out BotFailureReason? failReason)
            {
                request = default;
                failReason = BotFailureReason.NoLegalMovesInconsistentState;
                return false;
            }
        }

        private sealed class CountingNoLegalMovesStateReader : IUltimateBotStateReader
        {
            public int Calls { get; private set; }

            public bool TryBuildDecisionRequest(
                int botSlot,
                BotTurnId turnId,
                UltimateBotDifficultyProfileData profile,
                IBotRngSession rng,
                out UltimateBotDecisionRequest request,
                out BotFailureReason? failReason)
            {
                Calls++;
                request = default;
                failReason = BotFailureReason.NoLegalMovesInconsistentState;
                return false;
            }
        }

        private sealed class FakeEngine : IUltimateBotDecisionEngine
        {
            private readonly CellId _move;
            private readonly BotFailureReason? _degradationReason;
            private readonly int _searchDepthReached;
            private readonly int _iterationsCompleted;

            public FakeEngine(
                CellId? move = null,
                BotFailureReason? degradationReason = null,
                int searchDepthReached = 0,
                int iterationsCompleted = 0)
            {
                _move = move ?? default;
                _degradationReason = degradationReason;
                _searchDepthReached = searchDepthReached;
                _iterationsCompleted = iterationsCompleted;
            }

            public UniTask<UltimateBotDecisionResult> ChooseMoveAsync(UltimateBotDecisionRequest request, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                return UniTask.FromResult(new UltimateBotDecisionResult(_move, _degradationReason, false, null, SearchCutoffReason.Completed, string.Empty, _searchDepthReached, _iterationsCompleted));
            }
        }

        private sealed class BarrierEngine : IUltimateBotDecisionEngine
        {
            private readonly UniTaskCompletionSource _entered = new();
            private readonly UniTaskCompletionSource _release = new();

            public async UniTask<UltimateBotDecisionResult> ChooseMoveAsync(UltimateBotDecisionRequest request, CancellationToken ct)
            {
                _entered.TrySetResult();
                await _release.Task.AttachExternalCancellation(ct);
                return new UltimateBotDecisionResult(new CellId(0, 0), null, false, null, SearchCutoffReason.Completed, string.Empty, 1, 1);
            }

            public UniTask WaitUntilEnteredAsync()
            {
                return _entered.Task;
            }

            public void Release()
            {
                _release.TrySetResult();
            }
        }

        private sealed class RetryAwareEngine : IUltimateBotDecisionEngine
        {
            private int _calls;

            public UniTask<UltimateBotDecisionResult> ChooseMoveAsync(UltimateBotDecisionRequest request, CancellationToken ct)
            {
                _calls++;
                if (_calls >= 2)
                {
                    ct.ThrowIfCancellationRequested();
                }

                return UniTask.FromResult(new UltimateBotDecisionResult(
                    new CellId(0, 0),
                    null,
                    false,
                    null,
                    SearchCutoffReason.Completed,
                    string.Empty,
                    1,
                    1));
            }
        }

        private sealed class FakeSink : IBotMoveCommandSink
        {
            public bool TrySubmitMove(CellId move, BotTurnId turnId) => true;
        }

        private sealed class RetrySink : IBotMoveCommandSink
        {
            private readonly Action _onFirstSubmit;
            private int _calls;

            public RetrySink(Action onFirstSubmit)
            {
                _onFirstSubmit = onFirstSubmit;
            }

            public bool TrySubmitMove(CellId move, BotTurnId turnId)
            {
                _calls++;
                if (_calls == 1)
                {
                    _onFirstSubmit();
                    return false;
                }

                return true;
            }
        }

        private sealed class TrackingSink : IBotMoveCommandSink
        {
            private readonly int _rejectBeforeAccept;
            private readonly Action? _onReject;
            private int _calls;

            public TrackingSink(int rejectBeforeAccept = 0, Action? onReject = null)
            {
                _rejectBeforeAccept = rejectBeforeAccept;
                _onReject = onReject;
            }

            public int SubmitCount => _calls;
            public int AcceptedCount { get; private set; }

            public bool TrySubmitMove(CellId move, BotTurnId turnId)
            {
                _calls++;
                if (_calls <= _rejectBeforeAccept)
                {
                    _onReject?.Invoke();
                    return false;
                }

                AcceptedCount++;
                return true;
            }
        }

        private sealed class FallbackAssertingSink : IBotMoveCommandSink
        {
            private readonly CellId _expectedFallbackMove;
            private readonly Action _onFirstReject;

            public FallbackAssertingSink(CellId expectedFallbackMove, Action onFirstReject)
            {
                _expectedFallbackMove = expectedFallbackMove;
                _onFirstReject = onFirstReject;
            }

            public int SubmitCount { get; private set; }
            public CellId? AcceptedMove { get; private set; }

            public bool TrySubmitMove(CellId move, BotTurnId turnId)
            {
                SubmitCount++;
                if (SubmitCount == 1)
                {
                    _onFirstReject();
                    return false;
                }

                if (SubmitCount == 2)
                {
                    return false;
                }

                AcceptedMove = move;
                return move == _expectedFallbackMove;
            }
        }

        private sealed class CountingEngine : IUltimateBotDecisionEngine
        {
            public int Calls { get; private set; }

            public UniTask<UltimateBotDecisionResult> ChooseMoveAsync(UltimateBotDecisionRequest request, CancellationToken ct)
            {
                Calls++;
                return UniTask.FromResult(new UltimateBotDecisionResult(new CellId(0, 0), null, false, null, SearchCutoffReason.Completed, string.Empty, 1, 1));
            }
        }

        private sealed class SpyFailSafeGateway : IMatchFailSafeGateway
        {
            public int AbortCount { get; private set; }
            public bool IsInputLocked { get; private set; }

            public bool TryEnterAbortState(string userSafeMessageKey)
            {
                AbortCount++;
                IsInputLocked = true;
                return true;
            }

            public void ResetAbortState()
            {
                IsInputLocked = false;
            }
        }

        private sealed class RejectingFailSafeGateway : IMatchFailSafeGateway
        {
            public int Calls { get; private set; }
            public bool IsInputLocked => true;

            public bool TryEnterAbortState(string userSafeMessageKey)
            {
                Calls++;
                return false;
            }

            public void ResetAbortState()
            {
            }
        }
    }
}
