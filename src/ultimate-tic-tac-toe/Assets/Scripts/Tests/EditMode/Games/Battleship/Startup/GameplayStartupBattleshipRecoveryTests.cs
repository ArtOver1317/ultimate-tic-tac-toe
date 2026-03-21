#nullable enable

using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using Runtime.Gameplay.Shared;
using Runtime.Games.Battleship.Core;
using Tests.EditMode.Games.Battleship.Fakes;

namespace Tests.EditMode.Games.Battleship.Startup
{
    [TestFixture]
    [Category("Unit")]
    public sealed class GameplayStartupBattleshipRecoveryTests
    {
        [Test]
        public async Task WhenRecoverySnapshotApplied_ThenRestoresPhaseAndActiveSlot()
        {
            using var context = GameplayStartupBattleshipTestFactory.CreateContext(isHost: false);
            using var sut = context.CreateSut();

            await sut.StartAsync(CancellationToken.None);

            context.IncomingRecoverySnapshots.OnNext(GameplayStartupBattleshipTestFactory.CreateRecoveryMessage(
                senderUserId: "host-user",
                matchRoundId: 1,
                phase: BattleshipPhase.Battle,
                activePlayerSlot: PlayerSlotMapping.SlotO,
                placementTimerRemainingMs: 12000,
                moveTimerRemainingMs: 9000));
          
            await UniTask.DelayFrame(1);

            context.RecoveryStateApplier.CallCount.Should().Be(1);
            context.RecoveryStateApplier.LastState.Should().NotBeNull();
            context.RecoveryStateApplier.LastState!.Value.Phase.Should().Be(BattleshipPhase.Battle);
            context.RecoveryStateApplier.LastState!.Value.ActivePlayerSlot.Should().Be(PlayerSlotMapping.SlotO);
        }

        [Test]
        public async Task WhenRecoverySnapshotApplied_ThenRestoresBothTimers()
        {
            using var context = GameplayStartupBattleshipTestFactory.CreateContext(isHost: false);
            using var sut = context.CreateSut();

            await sut.StartAsync(CancellationToken.None);

            context.IncomingRecoverySnapshots.OnNext(GameplayStartupBattleshipTestFactory.CreateRecoveryMessage(
                senderUserId: "host-user",
                matchRoundId: 1,
                phase: BattleshipPhase.Battle,
                activePlayerSlot: PlayerSlotMapping.SlotO,
                placementTimerRemainingMs: 12000,
                moveTimerRemainingMs: 9000));
         
            await UniTask.DelayFrame(1);

            context.PlacementTimerService.RestoreCallCount.Should().Be(1);
            context.PlacementTimerService.LastRestoreRemainingSeconds.Should().Be(12f);
            context.MoveTimerService.RestoreCallCount.Should().Be(1);
            context.MoveTimerService.LastRestoreRemainingSeconds.Should().Be(9f);
            context.MoveTimerService.LastRestoreActivePlayerSlot.Should().Be(PlayerSlotMapping.SlotO);
        }

        [Test]
        public async Task WhenHostBindsBattleshipOnlineSession_ThenPublishesInitialRecoverySnapshot()
        {
            using var context = GameplayStartupBattleshipTestFactory.CreateContext(isHost: true);
            using var sut = context.CreateSut();

            await sut.StartAsync(CancellationToken.None);

            await context.BattleshipBridge.Received().SubmitRecoverySnapshotAsync(Arg.Any<Runtime.Games.Battleship.Networking.BattleshipRecoveryMessage>());
        }

        [Test]
        public async Task WhenRecoverySnapshotBelongsToDifferentMatchRound_ThenStartupIgnoresIt()
        {
            using var context = GameplayStartupBattleshipTestFactory.CreateContext(isHost: false);
            using var sut = context.CreateSut();

            await sut.StartAsync(CancellationToken.None);

            context.IncomingRecoverySnapshots.OnNext(GameplayStartupBattleshipTestFactory.CreateRecoveryMessage(
                senderUserId: "host-user",
                matchRoundId: 2,
                phase: BattleshipPhase.Battle,
                activePlayerSlot: PlayerSlotMapping.SlotO,
                placementTimerRemainingMs: 12000,
                moveTimerRemainingMs: 9000));
           
            await UniTask.DelayFrame(1);

            context.RecoveryStateApplier.CallCount.Should().Be(0);
            context.PlacementTimerService.RestoreCallCount.Should().Be(0);
            context.MoveTimerService.RestoreCallCount.Should().Be(0);
        }
    }
}