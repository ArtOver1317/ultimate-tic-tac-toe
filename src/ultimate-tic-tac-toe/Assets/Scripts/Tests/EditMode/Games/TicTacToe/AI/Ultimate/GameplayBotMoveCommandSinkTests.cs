#nullable enable

using System;
using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using R3;
using Runtime.Gameplay;
using Runtime.Gameplay.ECS;
using Runtime.Gameplay.Shared;
using Runtime.Games.TicTacToe.AI.Ultimate;
using Runtime.Games.TicTacToe.Moves;

namespace Tests.EditMode.Games.TicTacToe.AI.Ultimate
{
    [TestFixture]
    [Category("Unit")]
    public class GameplayBotMoveCommandSinkTests
    {
        [Test]
        public void WhenGameplayBotMoveSinkPreconditionsValid_ThenSubmitsExactlyOnce()
        {
            var move = new CellId(4, 4);
            var matchState = new FakeMatchStateProvider
            {
                IsMatchActiveValue = true,
                ActivePlayerSlotValue = 0,
                CommandSequenceValue = 10,
            };
            var failSafe = new FakeFailSafeGateway { IsInputLocked = false };
            var sut = new GameplayBotMoveCommandSink(matchState, failSafe);
            var turnId = BotTurnId.Build(10, 0);

            var submitted = sut.TrySubmitMove(move, turnId);

            submitted.Should().BeTrue();
            matchState.SubmittedCommands.Should().HaveCount(1);
            matchState.CommandSequence.Should().Be(11);
            var makeMove = matchState.SubmittedCommands[0].Should().BeOfType<MakeMoveCommand>().Subject;
            makeMove.CellId.Should().Be(move);
        }

        [Test]
        public void WhenGameplayBotMoveSinkInputLocked_ThenRejectsSubmit()
        {
            var matchState = CreateValidMatchState();
            var failSafe = new FakeFailSafeGateway { IsInputLocked = true };
            var turnId = BotTurnId.Build(20, 0);

            var sut = new GameplayBotMoveCommandSink(matchState, failSafe);
            var submitted = sut.TrySubmitMove(new CellId(0, 0), turnId);

            submitted.Should().BeFalse();
            matchState.SubmittedCommands.Should().BeEmpty();
            matchState.CommandSequence.Should().Be(20);
        }

        [Test]
        public void WhenGameplayBotMoveSinkMatchInactive_ThenRejectsSubmit()
        {
            var matchState = CreateValidMatchState();
            matchState.IsMatchActiveValue = false;
            var failSafe = new FakeFailSafeGateway { IsInputLocked = false };
            var turnId = BotTurnId.Build(20, 0);

            var sut = new GameplayBotMoveCommandSink(matchState, failSafe);
            var submitted = sut.TrySubmitMove(new CellId(0, 0), turnId);

            submitted.Should().BeFalse();
            matchState.SubmittedCommands.Should().BeEmpty();
            matchState.CommandSequence.Should().Be(20);
        }

        [Test]
        public void WhenGameplayBotMoveSinkCommandSequenceStale_ThenRejectsSubmit()
        {
            var matchState = CreateValidMatchState();
            var failSafe = new FakeFailSafeGateway { IsInputLocked = false };
            var turnId = BotTurnId.Build(19, 0);

            var sut = new GameplayBotMoveCommandSink(matchState, failSafe);
            var submitted = sut.TrySubmitMove(new CellId(0, 0), turnId);

            submitted.Should().BeFalse();
            matchState.SubmittedCommands.Should().BeEmpty();
            matchState.CommandSequence.Should().Be(20);
        }

        [Test]
        public void WhenGameplayBotMoveSinkActivePlayerSlotMismatch_ThenRejectsSubmit()
        {
            var matchState = CreateValidMatchState();
            var failSafe = new FakeFailSafeGateway { IsInputLocked = false };
            var turnId = BotTurnId.Build(20, 1);

            var sut = new GameplayBotMoveCommandSink(matchState, failSafe);
            var submitted = sut.TrySubmitMove(new CellId(0, 0), turnId);

            submitted.Should().BeFalse();
            matchState.SubmittedCommands.Should().BeEmpty();
            matchState.CommandSequence.Should().Be(20);
        }

        private static FakeMatchStateProvider CreateValidMatchState()
        {
            return new FakeMatchStateProvider
            {
                IsMatchActiveValue = true,
                ActivePlayerSlotValue = 0,
                CommandSequenceValue = 20,
            };
        }

        private sealed class FakeMatchStateProvider : IMatchStateProvider
        {
            public Subject<CellChangedEvent> CellChangedSubject { get; } = new();
            public Subject<LastMoveChangedEvent> LastMoveChangedSubject { get; } = new();
            public Subject<CurrentPlayerChangedEvent> CurrentPlayerChangedSubject { get; } = new();
            public Subject<CommandRejectedEvent> CommandRejectedSubject { get; } = new();
            public Subject<RoundFinishedEvent> RoundFinishedSubject { get; } = new();

            public Observable<CellChangedEvent> CellChanged => CellChangedSubject;
            public Observable<LastMoveChangedEvent> LastMoveChanged => LastMoveChangedSubject;
            public Observable<CurrentPlayerChangedEvent> CurrentPlayerChanged => CurrentPlayerChangedSubject;
            public Observable<CommandRejectedEvent> CommandRejected => CommandRejectedSubject;
            public Observable<RoundFinishedEvent> RoundFinished => RoundFinishedSubject;

            public bool IsMatchActiveValue { get; set; }
            public bool IsMatchActive => IsMatchActiveValue;

            public int ActivePlayerSlotValue { get; set; }
            public int ActivePlayerSlot => ActivePlayerSlotValue;

            public long CommandSequenceValue { get; set; }
            public long CommandSequence => CommandSequenceValue;

            public CellId? LastMove => null;

            public List<IGameplayCommand> SubmittedCommands { get; } = new();

            public int GetCellSlot(CellId cellId) => -1;
            public IReadOnlyList<CellSnapshot> GetAllCells() => Array.Empty<CellSnapshot>();

            public void SubmitCommand(IGameplayCommand command)
            {
                SubmittedCommands.Add(command);
                CommandSequenceValue++;
            }

            public void Dispose()
            {
            }
        }

        private sealed class FakeFailSafeGateway : IMatchFailSafeGateway
        {
            public bool IsInputLocked { get; set; }

            public bool TryEnterAbortState(string userSafeMessageKey)
            {
                IsInputLocked = true;
                return true;
            }

            public void ResetAbortState()
                => IsInputLocked = false;
        }
    }
}
