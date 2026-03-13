#nullable enable

using System;
using Runtime.Gameplay;
using Runtime.Gameplay.Shared;
using Runtime.Games.Battleship.Core;

namespace Runtime.Games.Battleship.Placement
{
    internal sealed class BattleshipPlacementTimeoutDispatcher
    {
        private const int SeedBaseMixMultiplier = 397;
        private const int PlayerSlotSeedMultiplier = 7919;
        private const int PlayerSlotSeedOffset = 1;

        private readonly IBattleshipGameplaySnapshotProvider _snapshotProvider;
        private readonly IGameplayCommandSink _commandSink;

        private bool _slot0TimeoutSubmitted;
        private bool _slot1TimeoutSubmitted;
        private int _seedBase;

        public BattleshipPlacementTimeoutDispatcher(
            IBattleshipGameplaySnapshotProvider snapshotProvider,
            IGameplayCommandSink commandSink)
        {
            _snapshotProvider = snapshotProvider ?? throw new ArgumentNullException(nameof(snapshotProvider));
            _commandSink = commandSink ?? throw new ArgumentNullException(nameof(commandSink));
        }

        public bool AreTimeoutsSubmittedForBothSlots => _slot0TimeoutSubmitted && _slot1TimeoutSubmitted;

        public void Reset()
        {
            _slot0TimeoutSubmitted = false;
            _slot1TimeoutSubmitted = false;
            _seedBase = unchecked((int)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        }

        public void SubmitTimeoutsForUnconfirmedPlayers()
        {
            SubmitPlacementTimeoutIfNeeded(PlayerSlotMapping.SlotX, ref _slot0TimeoutSubmitted);
            SubmitPlacementTimeoutIfNeeded(PlayerSlotMapping.SlotO, ref _slot1TimeoutSubmitted);
        }

        public bool HasUnconfirmedPlayers() =>
            !_snapshotProvider.IsPlacementConfirmed(PlayerSlotMapping.SlotX)
            || !_snapshotProvider.IsPlacementConfirmed(PlayerSlotMapping.SlotO);

        private void SubmitPlacementTimeoutIfNeeded(int playerSlot, ref bool alreadySubmitted)
        {
            if (alreadySubmitted)
                return;

            if (_snapshotProvider.IsPlacementConfirmed(playerSlot))
                return;

            alreadySubmitted = true;
            _commandSink.SubmitCommand(new PlacementTimeoutCommand(playerSlot, CreateAutoPlacementSeed(playerSlot)));
        }

        private int CreateAutoPlacementSeed(int playerSlot) =>
            unchecked((_seedBase * SeedBaseMixMultiplier) ^ (playerSlot + PlayerSlotSeedOffset) * PlayerSlotSeedMultiplier);
    }
}