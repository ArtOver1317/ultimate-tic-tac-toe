using FluentAssertions;
using NUnit.Framework;
using Runtime.GameModes.Wizard;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.Modes;

namespace Tests.EditMode.GameModes.Wizard
{
    [TestFixture]
    [Category("Unit")]
    public class GameLaunchConfigTests
    {
        [Test]
        public void WhenCtorCalledWithoutMoveTimeLimit_ThenMoveTimeLimitSecondsIsZero()
        {
            var sut = new GameLaunchConfig("classic", new TicTacToeConfig(3), new LocalHumanConfig());

            sut.MoveTimeLimitSeconds.Should().Be(0);
        }

        [Test]
        public void WhenCtorCalledWithNegativeMoveTimeLimit_ThenMoveTimeLimitSecondsClampedToZero()
        {
            var sut = new GameLaunchConfig("classic", new TicTacToeConfig(3), new LocalHumanConfig(), -15);

            sut.MoveTimeLimitSeconds.Should().Be(0);
        }
    }
}
