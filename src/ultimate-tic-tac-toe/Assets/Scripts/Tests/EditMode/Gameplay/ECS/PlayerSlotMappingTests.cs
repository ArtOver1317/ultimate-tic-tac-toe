using FluentAssertions;
using NUnit.Framework;
using Runtime.Gameplay.ECS;
using Runtime.Gameplay.Shared;
using Runtime.Games.TicTacToe.Moves;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.EditMode.Gameplay.ECS
{
    [TestFixture]
    [Category("Unit")]
    public class PlayerSlotMappingTests
    {
        [Test]
        public void WhenSlotMappedToMarkAndBack_ThenReturnsOriginalSlot()
        {
            var xSlot = PlayerSlotMapping.MarkToSlot(PlayerSlotMapping.SlotToMark(PlayerSlotMapping.SlotX));
            var oSlot = PlayerSlotMapping.MarkToSlot(PlayerSlotMapping.SlotToMark(PlayerSlotMapping.SlotO));

            xSlot.Should().Be(PlayerSlotMapping.SlotX);
            oSlot.Should().Be(PlayerSlotMapping.SlotO);
        }

        [Test]
        public void WhenEmptySentinelMapped_ThenReturnsFallbackValues()
        {
            PlayerSlotMapping.MarkToSlot(PlayerMark.None).Should().Be(-1);
            PlayerSlotMapping.SlotToMark(-1).Should().Be(PlayerMark.None);
        }

        [Test]
        public void WhenInvalidSlotOrMarkMapped_ThenReturnsFallbackValuesAndLogsErrors()
        {
            LogAssert.Expect(LogType.Error,
                "[Infrastructure] [PlayerSlotMapping] Invalid player slot: 42.");
            LogAssert.Expect(LogType.Error,
                "[Infrastructure] [PlayerSlotMapping] Invalid player mark: 99.");

            PlayerSlotMapping.SlotToMark(42).Should().Be(PlayerMark.None);
            PlayerSlotMapping.MarkToSlot((PlayerMark)99).Should().Be(-1);
        }
    }
}
