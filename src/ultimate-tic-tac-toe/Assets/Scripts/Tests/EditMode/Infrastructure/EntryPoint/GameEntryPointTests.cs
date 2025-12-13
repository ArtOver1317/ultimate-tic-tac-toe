using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
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
        public async Task WhenStartAsync_ThenEntersBootstrapState()
        {
            // Arrange
            var stateMachine = Substitute.For<IGameStateMachine>();
            stateMachine.EnterAsync<BootstrapState>(Arg.Any<CancellationToken>()).Returns(UniTask.CompletedTask);
            var sut = new GameEntryPoint(stateMachine);

            // Act
            await sut.StartAsync(CancellationToken.None);

            // Assert
            await stateMachine.Received(1).EnterAsync<BootstrapState>(Arg.Any<CancellationToken>());
        }
    }
}
