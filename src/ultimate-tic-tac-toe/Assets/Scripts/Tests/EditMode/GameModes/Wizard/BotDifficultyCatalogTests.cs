using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Runtime.GameModes.Wizard;

namespace Tests.EditMode.GameModes.Wizard
{
    [TestFixture]
    [Category("Unit")]
    public class BotDifficultyCatalogTests
    {
        [Test]
        public void WhenCreated_ThenDifficultiesHasUniqueIdsAndIsNotEmpty()
        {
            // Arrange
            var catalog = new BotDifficultyCatalog();

            // Act
            var difficulties = catalog.Difficulties;

            // Assert
            difficulties.Should().NotBeNull();
            difficulties.Should().NotBeEmpty();
            difficulties.Select(d => d.Id).Distinct().Count().Should().Be(difficulties.Count);
        }
    }
}
