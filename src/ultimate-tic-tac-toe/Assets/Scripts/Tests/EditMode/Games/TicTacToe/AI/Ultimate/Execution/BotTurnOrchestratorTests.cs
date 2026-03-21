#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using R3;
using Runtime.Gameplay;
using Runtime.Gameplay.Shared;
using Runtime.Games.TicTacToe.AI.Ultimate.Core;
using Runtime.Games.TicTacToe.AI.Ultimate.Execution;
using Runtime.Games.TicTacToe.Ultimate.Rules;
using UnityEngine.TestTools;
using EcsGameStatus = Runtime.Gameplay.Shared.EcsGameStatus;

namespace Tests.EditMode.Games.TicTacToe.AI.Ultimate.Execution
{
    [TestFixture]
    [Category("Unit")]
    public partial class BotTurnOrchestratorTests
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
            BotTurnId? inFlightAtPublish = null;
            
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

        private static async UniTask WaitUntilAsync(Func<bool> condition)
        {
            for (var i = 0; i < 120; i++)
            {
                if (condition()) 
                    return;

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

            public void EmitCurrentPlayerChanged(int slot) => _player.OnNext(new CurrentPlayerChangedEvent(slot));

            public void EmitRoundFinished() => _finished.OnNext(new RoundFinishedEvent(EcsGameStatus.Draw, null, null));
        }

        private sealed class FakeSnapshot : IGameplaySnapshotProvider
        {
            public int GetCellSlot(CellId cellId) => -1;
            public IReadOnlyList<CellSnapshot> GetAllCells() => Array.Empty<CellSnapshot>();
            public long CommandSequence { get; set; } = 10;
            public int ActivePlayerSlot { get; set; }
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

            public UniTask WaitUntilEnteredAsync() => _entered.Task;

            public void Release() => _release.TrySetResult();
        }

        private sealed class RetryAwareEngine : IUltimateBotDecisionEngine
        {
            private int _calls;

            public UniTask<UltimateBotDecisionResult> ChooseMoveAsync(UltimateBotDecisionRequest request, CancellationToken ct)
            {
                _calls++;
                
                if (_calls >= 2) 
                    ct.ThrowIfCancellationRequested();

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

            public RetrySink(Action onFirstSubmit) => _onFirstSubmit = onFirstSubmit;

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
                    return false;

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

            public void ResetAbortState() => IsInputLocked = false;
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

            public void ResetAbortState() { }
        }
    }
}
