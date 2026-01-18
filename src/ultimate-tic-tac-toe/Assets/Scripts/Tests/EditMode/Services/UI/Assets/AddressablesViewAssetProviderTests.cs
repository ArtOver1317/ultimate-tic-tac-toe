using System;
using FluentAssertions;
using NUnit.Framework;
using Runtime.Services.UI.Assets;

namespace Tests.EditMode.Services.UI.Assets
{
    [Category("Unit")]
    [TestFixture]
    public class AddressablesViewAssetProviderTests
    {
        private AddressablesViewAssetProvider _provider;

        [SetUp]
        public void SetUp() => _provider = new AddressablesViewAssetProvider();

        [TearDown]
        public void TearDown() => _provider = null;

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void WhenLoadVisualTreeCalledWithNullOrWhitespaceKey_ThenThrowsArgumentException(string key)
        {
            // Arrange
            // Act
            Action act = () => _provider.LoadVisualTreeAsync(key, default);

            // Assert
            act.Should().Throw<ArgumentException>();
        }
    }
}
