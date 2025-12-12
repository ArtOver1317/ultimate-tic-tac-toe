using NSubstitute;
using NUnit.Framework;
using Runtime.Infrastructure.EntryPoint;
using Runtime.Infrastructure.GameStateMachine;
using Runtime.Infrastructure.GameStateMachine.States;

namespace Tests.EditMode.Infrastructure.EntryPoint
{
    [TestFixture]
    public class GameEntryPointTests
    {
        [Test]
        public void WhenStart_ThenEntersBootstrapState()
        {
            // Arrange
            var stateMachine = Substitute.For<IGameStateMachine>();
            var sut = new GameEntryPoint(stateMachine);

            // Act
            sut.Start();

            // Assert
            stateMachine.Received(1).Enter<BootstrapState>();
        }
    }
}
