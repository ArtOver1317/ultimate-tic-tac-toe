using System;
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

        [Test]
        public void WhenGetReturnCycle_ThenObjectsAreReusedInLIFO()
        {
            // Arrange
            var item1 = new TestClass();
            var item2 = new TestClass();
            var item3 = new TestClass();

            // Act & Assert - Return 3 items
            _pool.Return(item1);
            _pool.Return(item2);
            _pool.Return(item3);
            _pool.GetSize(typeof(TestClass)).Should().Be(3);

            // Act & Assert - Get in LIFO order (Last In First Out)
            var retrieved3 = _pool.Get<TestClass>();
            retrieved3.Should().BeSameAs(item3); // last returned
            _pool.GetSize(typeof(TestClass)).Should().Be(2);

            var retrieved2 = _pool.Get<TestClass>();
            retrieved2.Should().BeSameAs(item2);
            _pool.GetSize(typeof(TestClass)).Should().Be(1);

            var retrieved1 = _pool.Get<TestClass>();
            retrieved1.Should().BeSameAs(item1); // first returned
            _pool.GetSize(typeof(TestClass)).Should().Be(0);

            // Act & Assert - Return and Get again (reuse)
            _pool.Return(item2);
            _pool.GetSize(typeof(TestClass)).Should().Be(1);

            var reusedItem2 = _pool.Get<TestClass>();
            reusedItem2.Should().BeSameAs(item2); // same instance
            _pool.GetSize(typeof(TestClass)).Should().Be(0);
        }

        #endregion

        #region Callback Tests

        [Test]
        public void WhenReturnWithCallbacks_ThenInvokedCorrectly()
        {
            // Arrange
            var item1 = new TestClass();
            var item2 = new TestClass();
            var onReturnCallCount = 0;
            var onClearCallCount = 0;

            // Act - Return with callback
            _pool.Return(item1, _ => onReturnCallCount++);
            _pool.Return(item2, _ => onReturnCallCount++);

            // Assert - onReturn callback invoked twice
            onReturnCallCount.Should().Be(2);

            // Act - Clear with callback
            _pool.Clear(typeof(TestClass), _ => onClearCallCount++);

            // Assert - onClear callback invoked twice (once per item)
            onClearCallCount.Should().Be(2);
            _pool.GetSize(typeof(TestClass)).Should().Be(0);
        }

        #endregion

        #region Return Null Validation Tests

        [Test]
        public void WhenReturnWithNullTypeAndItem_ThenThrowsException()
        {
            // Arrange
            // Pool готов

            // Act
            Action act = () => _pool.Return(null, null);

            // Assert
            act.Should().Throw<ArgumentNullException>()
                .WithMessage("*type*");
        }

        [Test]
        public void WhenReturnWithNullTypeButValidItem_ThenUsesItemType()
        {
            // Arrange
            var item = new TestClass();

            // Act
            _pool.Return(null, item);

            // Assert
            _pool.GetSize(item.GetType()).Should().Be(1);
            _pool.Get<TestClass>().Should().BeSameAs(item);
        }

        #endregion

        #region Test Classes

        private class TestClass { }

        #endregion
    }
}

