using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using Runtime.Infrastructure.GameStateMachine;
using Runtime.Infrastructure.GameStateMachine.States;
using VContainer;

namespace Tests.EditMode.Infrastructure.GameStateMachine
{
    [TestFixture]
    public class StateFactoryTests
    {
        [Test]
        public void WhenCreateState_ThenResolvesStateFromContainer()
        {
            // Arrange
            var container = Substitute.For<IObjectResolver>();
            var expectedState = Substitute.For<IExitableState>();
            container.Resolve<IExitableState>().Returns(expectedState);
            var sut = new StateFactory(container);

            // Act
            var result = sut.CreateState<IExitableState>();

            // Assert
            result.Should().Be(expectedState);
            container.Received(1).Resolve<IExitableState>();
        }
    }
}