using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Runtime.GameModes.Wizard;

namespace Tests.EditMode.GameModes.Wizard
{
    [TestFixture]
    [Category("Stress")]
    public class GameModeSessionStressTests
    {
        [Test]
        public async Task WhenConcurrentUpdatesHappen_ThenDoesNotThrowAndVersionMonotonicallyIncreases()
        {
            // Arrange
            using var sut = new GameModeSession(GameModeSessionSnapshot.Default);
            var initialVersion = sut.Snapshot.CurrentValue.Version;
            const int n = 20;

            // Act
            var tasks = Enumerable.Range(0, n)
                .Select(_ => Task.Run(() => sut.Update(s => s)))
                .ToArray();

            await Task.WhenAll(tasks);

            // Assert
            sut.Snapshot.CurrentValue.Version.Should().Be(initialVersion + n);
        }
    }
}
