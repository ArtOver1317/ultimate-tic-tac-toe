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

        [Test]
        public void WhenReturnWithExplicitType_ThenUsesProvidedType()
        {
            // Arrange
            var derivedItem = new DerivedClass();

            // Act
            _pool.Return(typeof(BaseClass), derivedItem);

            // Assert
            _pool.GetSize(typeof(BaseClass)).Should().Be(1);
            _pool.GetSize(typeof(DerivedClass)).Should().Be(0);
        }

        #endregion

        #region Type Isolation Tests

        [Test]
        public void WhenDifferentTypes_ThenPoolsAreIsolated()
        {
            // Arrange
            var classAItem1 = new ClassA();
            var classAItem2 = new ClassA();
            var classBItem1 = new ClassB();

            // Act
            _pool.Return(classAItem1);
            _pool.Return(classAItem2);
            _pool.Return(classBItem1);

            // Assert - Pool sizes are correct
            _pool.GetSize(typeof(ClassA)).Should().Be(2);
            _pool.GetSize(typeof(ClassB)).Should().Be(1);

            // Assert - Get returns correct type
            var retrievedA = _pool.Get<ClassA>();
            retrievedA.Should().BeOfType<ClassA>();
            retrievedA.Should().BeSameAs(classAItem2); // LIFO

            var retrievedB = _pool.Get<ClassB>();
            retrievedB.Should().BeOfType<ClassB>();
            retrievedB.Should().BeSameAs(classBItem1);

            // Assert - Pools are independent
            _pool.GetSize(typeof(ClassA)).Should().Be(1); // still has 1 item
            _pool.GetSize(typeof(ClassB)).Should().Be(0); // empty now
        }

        #endregion

        #region Clear Tests

        [Test]
        public void WhenClearType_ThenOnlyThatPoolIsCleared()
        {
            // Arrange
            var classAItem1 = new ClassA();
            var classAItem2 = new ClassA();
            var classBItem1 = new ClassB();

            _pool.Return(classAItem1);
            _pool.Return(classAItem2);
            _pool.Return(classBItem1);

            // Act
            _pool.Clear(typeof(ClassA));

            // Assert
            _pool.GetSize(typeof(ClassA)).Should().Be(0);
            _pool.GetSize(typeof(ClassB)).Should().Be(1);
        }

        [Test]
        public void WhenClearAll_ThenAllPoolsAreCleared()
        {
            // Arrange
            var classAItem1 = new ClassA();
            var classAItem2 = new ClassA();
            var classBItem1 = new ClassB();
            var classBItem2 = new ClassB();
            var classBItem3 = new ClassB();

            _pool.Return(classAItem1);
            _pool.Return(classAItem2);
            _pool.Return(classBItem1);
            _pool.Return(classBItem2);
            _pool.Return(classBItem3);

            // Act
            _pool.ClearAll();

            // Assert
            _pool.GetSize(typeof(ClassA)).Should().Be(0);
            _pool.GetSize(typeof(ClassB)).Should().Be(0);
            _pool.GetStats().Count.Should().Be(0);
        }

        #endregion

        #region GetSize Tests

        [Test]
        public void WhenGetSizeWithNull_ThenThrowsException()
        {
            // Arrange
            // Pool готов

            // Act
            Action act = () => _pool.GetSize(null);

            // Assert
            act.Should().Throw<ArgumentNullException>();
        }

        #endregion

        #region GetStats Tests

        [Test]
        public void WhenGetStats_ThenReturnsAllPoolSizes()
        {
            // Arrange
            var classAItem1 = new ClassA();
            var classAItem2 = new ClassA();
            var classBItem1 = new ClassB();
            var classBItem2 = new ClassB();
            var classBItem3 = new ClassB();

            _pool.Return(classAItem1);
            _pool.Return(classAItem2);
            _pool.Return(classBItem1);
            _pool.Return(classBItem2);
            _pool.Return(classBItem3);

            // Act
            var stats = _pool.GetStats();

            // Assert
            stats.Count.Should().Be(2);
            stats[typeof(ClassA)].Should().Be(2);
            stats[typeof(ClassB)].Should().Be(3);
        }

        #endregion

        #region Get Overload Tests

        [Test]
        public void WhenGetWithMatchingType_ThenReturnsItem()
        {
            // Arrange
            var item = new TestClass();
            _pool.Return(item);

            // Act
            var result = _pool.Get<TestClass>(typeof(TestClass));

            // Assert
            result.Should().BeSameAs(item);
        }

        [Test]
        public void WhenGetWithNullType_ThenStillWorks()
        {
            // Arrange
            var item = new TestClass();
            _pool.Return(item);

            // Act
            var result = _pool.Get<TestClass>(null);

            // Assert
            result.Should().BeSameAs(item);
        }

        #endregion

        #region Test Classes

        private class TestClass { }

        private class BaseClass { }

        private class DerivedClass : BaseClass { }

        private class ClassA { }

        private class ClassB { }

        #endregion
    }
}

