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
using Runtime.Games.TicTacToe.AI.Ultimate;
using Runtime.Games.TicTacToe.Moves;
using Runtime.Games.TicTacToe.Rules;
using Runtime.Games.TicTacToe.Ultimate.Rules;

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
            var sink = new FakeSink();
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
                    0,
                    false,
                    EvaluationWeights.Default);
                return true;
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
                    new UltimateBoardSnapshot(cells, miniBoards, AllowedMajors.All, 0, default, false, Runtime.Games.TicTacToe.Rules.GameStatus.InProgress),
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
            public UniTask<UltimateBotDecisionResult> ChooseMoveAsync(UltimateBotDecisionRequest request, CancellationToken ct)
            {
                return UniTask.FromResult(new UltimateBotDecisionResult(default, null, false, null, 0, SearchCutoffReason.Completed, string.Empty, 0, 0, 0));
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
                    1,
                    SearchCutoffReason.Completed,
                    string.Empty,
                    1,
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
