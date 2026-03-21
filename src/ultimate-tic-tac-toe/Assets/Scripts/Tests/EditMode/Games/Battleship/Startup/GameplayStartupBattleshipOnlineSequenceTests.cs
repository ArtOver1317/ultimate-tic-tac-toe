#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using Runtime.GameModes.Wizard.Online;
using Runtime.Gameplay;
using Runtime.Gameplay.Shared;
using Tests.EditMode.Games.Battleship.Fakes;

namespace Tests.EditMode.Games.Battleship.Startup
{
    [TestFixture]
    [Category("Unit")]
    public sealed class GameplayStartupBattleshipOnlineSequenceTests
    {
        [Test]
        public async Task WhenHostReceivesGuestShotSequenceWithGap_ThenRejectsSecondMove()
        {
            using var context = GameplayStartupBattleshipTestFactory.CreateContext();
            var forwardedCommands = new List<IGameplayCommand>();
           
            context.MatchStateProvider
                .When(provider => provider.SubmitCommand(Arg.Any<IGameplayCommand>()))
                .Do(callInfo => forwardedCommands.Add(callInfo.Arg<IGameplayCommand>()));

            using var sut = context.CreateSut();

            await sut.StartAsync(CancellationToken.None);

            context.IncomingMoves.OnNext(new MoveCommand(Guid.NewGuid(), "guest-user", cellIndex: 0, clientTick: 1));
            await UniTask.DelayFrame(1);

            context.IncomingMoves.OnNext(new MoveCommand(Guid.NewGuid(), "guest-user", cellIndex: 1, clientTick: 3));
            await UniTask.DelayFrame(1);

            var forwardedMoves = forwardedCommands.Should().ContainSingle(command => command is MakeMoveCommand).Subject;
            forwardedMoves.Should().BeOfType<MakeMoveCommand>();
            ((MakeMoveCommand)forwardedMoves).CellId.Should().Be(new CellId(0, 0));
        }

        [Test]
        public async Task WhenHostReceivesGuestShotSequenceStrictlyIncreasingByOne_ThenAcceptsBothMoves()
        {
            using var context = GameplayStartupBattleshipTestFactory.CreateContext();
            var forwardedCommands = new List<IGameplayCommand>();
           
            context.MatchStateProvider
                .When(provider => provider.SubmitCommand(Arg.Any<IGameplayCommand>()))
                .Do(callInfo => forwardedCommands.Add(callInfo.Arg<IGameplayCommand>()));

            using var sut = context.CreateSut();

            await sut.StartAsync(CancellationToken.None);

            context.IncomingMoves.OnNext(new MoveCommand(Guid.NewGuid(), "guest-user", cellIndex: 0, clientTick: 1));
            await UniTask.DelayFrame(1);

            context.IncomingMoves.OnNext(new MoveCommand(Guid.NewGuid(), "guest-user", cellIndex: 1, clientTick: 2));
            await UniTask.DelayFrame(1);

            forwardedCommands.Should().HaveCount(2);
            forwardedCommands.Should().OnlyContain(command => command is MakeMoveCommand);
            ((MakeMoveCommand)forwardedCommands[0]).CellId.Should().Be(new CellId(0, 0));
            ((MakeMoveCommand)forwardedCommands[1]).CellId.Should().Be(new CellId(0, 1));
        }

        [Test]
        public async Task WhenHostReceivesShotOutOfTurn_ThenDoesNotForwardToLocalSink()
        {
            using var context = GameplayStartupBattleshipTestFactory.CreateContext(isHost: true, activePlayerSlot: PlayerSlotMapping.SlotX);
            using var sut = context.CreateSut();

            await sut.StartAsync(CancellationToken.None);

            context.IncomingMoves.OnNext(new MoveCommand(Guid.NewGuid(), "guest-user", cellIndex: 0, clientTick: 1));
            await UniTask.DelayFrame(1);

            context.MatchStateProvider.DidNotReceive().SubmitCommand(Arg.Any<IGameplayCommand>());
        }

        [Test]
        public async Task WhenHostReceivesDuplicateShotSequence_ThenSecondMoveIsIgnored()
        {
            using var context = GameplayStartupBattleshipTestFactory.CreateContext(isHost: true, activePlayerSlot: PlayerSlotMapping.SlotO);
            var forwardedCommands = new List<IGameplayCommand>();
          
            context.MatchStateProvider
                .When(provider => provider.SubmitCommand(Arg.Any<IGameplayCommand>()))
                .Do(callInfo => forwardedCommands.Add(callInfo.Arg<IGameplayCommand>()));

            using var sut = context.CreateSut();

            await sut.StartAsync(CancellationToken.None);

            context.IncomingMoves.OnNext(new MoveCommand(Guid.NewGuid(), "guest-user", cellIndex: 0, clientTick: 1));
            await UniTask.DelayFrame(1);

            context.IncomingMoves.OnNext(new MoveCommand(Guid.NewGuid(), "guest-user", cellIndex: 1, clientTick: 1));
            await UniTask.DelayFrame(1);

            var forwardedMoves = forwardedCommands.Should().ContainSingle(command => command is MakeMoveCommand).Subject;
            ((MakeMoveCommand)forwardedMoves).CellId.Should().Be(new CellId(0, 0));
        }

        [Test]
        public async Task WhenHostReceivesStaleShotSequence_ThenMoveIsIgnored()
        {
            using var context = GameplayStartupBattleshipTestFactory.CreateContext(isHost: true, activePlayerSlot: PlayerSlotMapping.SlotO);
            var forwardedCommands = new List<IGameplayCommand>();
           
            context.MatchStateProvider
                .When(provider => provider.SubmitCommand(Arg.Any<IGameplayCommand>()))
                .Do(callInfo => forwardedCommands.Add(callInfo.Arg<IGameplayCommand>()));

            using var sut = context.CreateSut();

            await sut.StartAsync(CancellationToken.None);

            context.IncomingMoves.OnNext(new MoveCommand(Guid.NewGuid(), "guest-user", cellIndex: 0, clientTick: 1));
            await UniTask.DelayFrame(1);
            context.IncomingMoves.OnNext(new MoveCommand(Guid.NewGuid(), "guest-user", cellIndex: 1, clientTick: 2));
            await UniTask.DelayFrame(1);
            context.IncomingMoves.OnNext(new MoveCommand(Guid.NewGuid(), "guest-user", cellIndex: 2, clientTick: 3));
            await UniTask.DelayFrame(1);

            context.IncomingMoves.OnNext(new MoveCommand(Guid.NewGuid(), "guest-user", cellIndex: 3, clientTick: 1));
            await UniTask.DelayFrame(1);

            forwardedCommands.Should().HaveCount(3);
            forwardedCommands.Should().OnlyContain(command => command is MakeMoveCommand);
            ((MakeMoveCommand)forwardedCommands[0]).CellId.Should().Be(new CellId(0, 0));
            ((MakeMoveCommand)forwardedCommands[1]).CellId.Should().Be(new CellId(0, 1));
            ((MakeMoveCommand)forwardedCommands[2]).CellId.Should().Be(new CellId(0, 2));
        }
    }
}