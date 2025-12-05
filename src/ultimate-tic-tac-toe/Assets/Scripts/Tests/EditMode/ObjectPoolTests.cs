using FluentAssertions;
using NUnit.Framework;
using Runtime.Services.UI;

namespace Tests.EditMode
{
    [TestFixture]
    public class ObjectPoolTests
    {
        private ObjectPool<object> _pool;

        [SetUp]
        public void SetUp() => _pool = new ObjectPool<object>();

        #region Get Tests

        [Test]
        public void WhenPoolIsEmpty_ThenGetReturnsNull()
        {
            // Arrange
            // Pool is empty by default

            // Act
            var result = _pool.Get<TestClass>();

            // Assert
            result.Should().BeNull();
        }

        #endregion

        #region Test Classes

        private class TestClass { }

        #endregion
    }
}

