using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Runtime.GameModes.Wizard;

namespace Tests.EditMode.GameModes.Wizard
{
    [TestFixture]
    [Category("Unit")]
    public class MatchmakingServiceStubTests
    {
        private MatchmakingServiceStub _sut;
        private MatchmakingRequest _request;

        [SetUp]
        public void SetUp()
        {
            _sut = new MatchmakingServiceStub();
            _request = new MatchmakingRequest("classic", new TicTacToeConfig(3));
        }

        [TearDown]
        public void TearDown()
        {
            _sut = null;
            _request = null;
        }

        [Test]
        public async Task WhenEnterQueueAsyncCalledWithValidRequest_ThenThrowsNotSupportedException()
        {
            // Arrange
            Func<Task> act = async () => await _sut.EnterQueueAsync(_request, CancellationToken.None);

            // Act / Assert
            await act.Should().ThrowAsync<NotSupportedException>();
        }

        [Test]
        public async Task WhenWaitForMatchAsyncCalledWithValidEntry_ThenThrowsNotSupportedException()
        {
            // Arrange
            var entry = new QueueEntry("room-1", immediateResult: null);
            Func<Task> act = async () => await _sut.WaitForMatchAsync(entry, CancellationToken.None);

            // Act / Assert
            await act.Should().ThrowAsync<NotSupportedException>();
        }
    }
}
