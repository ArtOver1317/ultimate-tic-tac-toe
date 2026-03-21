using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using Runtime.GameModes.Wizard.Matchmaking.Contracts;
using Runtime.GameModes.Wizard.Matchmaking.Runtime;
using Runtime.GameModes.Wizard.Modes;

namespace Tests.EditMode.GameModes.Wizard.Matchmaking
{
    [TestFixture]
    [Category("Unit")]
    public class MatchmakingFsmValidationTests
    {
        private IMatchmakingService _service;
        private MatchmakingFsm _sut;

        [SetUp]
        public void SetUp()
        {
            _service = Substitute.For<IMatchmakingService>();
            _sut = new MatchmakingFsm(_service);
        }

        [TearDown]
        public void TearDown()
        {
            _sut?.Dispose();
            _sut = null;
        }

        [Test]
        public void WhenConstructedWithNullService_ThenThrowsArgumentNullException()
        {
            // Arrange
            Action act = () => _ = new MatchmakingFsm(null);

            // Act / Assert
            act.Should().Throw<ArgumentNullException>();
        }

        [Test]
        public void WhenConstructedWithZeroTimeout_ThenThrowsArgumentOutOfRangeException()
        {
            // Arrange
            Action act = () => _ = new MatchmakingFsm(_service, TimeSpan.Zero);

            // Act / Assert
            act.Should().Throw<ArgumentOutOfRangeException>();
        }

        [Test]
        public void WhenConstructedWithNegativeTimeout_ThenThrowsArgumentOutOfRangeException()
        {
            // Arrange
            Action act = () => _ = new MatchmakingFsm(_service, TimeSpan.FromSeconds(-1));

            // Act / Assert
            act.Should().Throw<ArgumentOutOfRangeException>();
        }

        [Test]
        public async Task WhenTryStartSearchAsyncCalledWithNullRequest_ThenThrowsArgumentNullException()
        {
            // Arrange
            Func<Task> act = async () => await _sut.TryStartSearchAsync(null, CancellationToken.None);

            // Act / Assert
            await act.Should().ThrowAsync<ArgumentNullException>();
        }

        [Test]
        public async Task WhenTryStartSearchAsyncCalledWithZeroTimeout_ThenThrowsArgumentOutOfRangeException()
        {
            // Arrange
            var request = new MatchmakingRequest("classic", new TicTacToeConfig(3));
            Func<Task> act = async () => await _sut.TryStartSearchAsync(request, TimeSpan.Zero, CancellationToken.None);

            // Act / Assert
            await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        }
    }
}
