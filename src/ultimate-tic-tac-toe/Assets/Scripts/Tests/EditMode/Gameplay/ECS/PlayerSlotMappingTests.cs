using FluentAssertions;
using NUnit.Framework;
using Runtime.Gameplay.ECS;
using Runtime.Games.TicTacToe.Moves;

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
        public void WhenInvalidSlotOrMarkMapped_ThenReturnsFallbackValues()
        {
            PlayerSlotMapping.SlotToMark(42).Should().Be(PlayerMark.None);
            PlayerSlotMapping.MarkToSlot(PlayerMark.None).Should().Be(-1);
        }
    }
}
