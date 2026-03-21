#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using R3;
using Runtime.Gameplay;
using Runtime.Games.TicTacToe.AI.Ultimate.Core;
using Runtime.Games.TicTacToe.AI.Ultimate.Execution;

namespace Tests.EditMode.Games.TicTacToe.AI.Ultimate.Execution
{
    public partial class BotTurnOrchestratorTests
    {
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
    }
}