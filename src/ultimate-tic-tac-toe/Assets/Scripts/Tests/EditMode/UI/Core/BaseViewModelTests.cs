using System;
using FluentAssertions;
using NUnit.Framework;
using R3;
using Runtime.UI.Core;

namespace Tests.EditMode
{
    [TestFixture]
    public class BaseViewModelTests
    {
        private TestViewModel _viewModel;

        [SetUp]
        public void SetUp() => _viewModel = new TestViewModel();

        [TearDown]
        public void TearDown() => _viewModel?.Dispose();

        #region Reset Tests

        [Test]
        public void WhenResetCalled_ThenDisposablesAreCleared()
        {
            // Arrange
            var disposable1 = new MockDisposable();
            var disposable2 = new MockDisposable();
            var disposable3 = new MockDisposable();

            _viewModel.AddDisposable(disposable1);
            _viewModel.AddDisposable(disposable2);
            _viewModel.AddDisposable(disposable3);

            // Act
            _viewModel.Reset();

            // Assert
            disposable1.DisposeCallCount.Should().Be(1, "first disposable should be disposed once");
            disposable2.DisposeCallCount.Should().Be(1, "second disposable should be disposed once");
            disposable3.DisposeCallCount.Should().Be(1, "third disposable should be disposed once");

            // Verify that new disposables can be added after reset and old ones are not disposed again
            var disposable4 = new MockDisposable();
            _viewModel.AddDisposable(disposable4);

            const string oldDisposablesShouldNotBeDisposedAgain =
                "old disposables should not be disposed again";
            
            disposable1.DisposeCallCount.Should().Be(1, oldDisposablesShouldNotBeDisposedAgain);
            disposable2.DisposeCallCount.Should().Be(1, oldDisposablesShouldNotBeDisposedAgain);
            disposable3.DisposeCallCount.Should().Be(1, oldDisposablesShouldNotBeDisposedAgain);
        }

        [Test]
        public void WhenResetCalled_ThenOnCloseRequestedEmitsEvent()
        {
            // Arrange
            var eventCount = 0;
            _viewModel.OnCloseRequested.Subscribe(_ => eventCount++);

            // Act
            _viewModel.Reset();

            // Assert
            eventCount.Should().Be(1, "Reset should signal OnCloseRequested to end the VM session");
        }

        [Test]
        public void WhenResetCalled_ThenOnResetIsInvoked()
        {
            // Arrange
            _viewModel.WasOnResetCalled.Should().BeFalse("initially OnReset should not be called");

            // Act
            _viewModel.Reset();

            // Assert
            _viewModel.WasOnResetCalled.Should().BeTrue("OnReset hook should be invoked during Reset");
        }

        #endregion

        #region Dispose Tests

        [Test]
        public void WhenDisposeCalled_ThenOnDisposeIsInvoked()
        {
            // Arrange
            _viewModel.WasOnDisposeCalled.Should().BeFalse("initially OnDispose should not be called");

            // Act
            _viewModel.Dispose();

            // Assert
            _viewModel.WasOnDisposeCalled.Should().BeTrue("OnDispose hook should be invoked during Dispose");
        }

        [Test]
        public void WhenDisposeCalled_ThenAllDisposablesAreDisposed()
        {
            // Arrange
            var disposable1 = new MockDisposable();
            var disposable2 = new MockDisposable();
            var disposable3 = new MockDisposable();

            _viewModel.AddDisposable(disposable1);
            _viewModel.AddDisposable(disposable2);
            _viewModel.AddDisposable(disposable3);

            // Act
            _viewModel.Dispose();

            // Assert
            disposable1.DisposeCallCount.Should().Be(1, "first disposable should be disposed once");
            disposable2.DisposeCallCount.Should().Be(1, "second disposable should be disposed once");
            disposable3.DisposeCallCount.Should().Be(1, "third disposable should be disposed once");
        }

        [Test]
        public void WhenDisposeCalled_ThenOnCloseRequestedIsDisposed()
        {
            // Arrange
            var eventReceived = false;
            var subscription = _viewModel.OnCloseRequested.Subscribe(_ => eventReceived = true);

            // Act
            _viewModel.Dispose();

            // Assert - attempting to subscribe after dispose should throw
            Action subscribeAfterDispose = () => _viewModel.OnCloseRequested.Subscribe(_ => { });

            subscribeAfterDispose.Should().Throw<ObjectDisposedException>(
                "OnCloseRequested should be disposed and reject new subscriptions");

            // Verify original subscription was also disposed
            subscription.Dispose();
            eventReceived.Should().BeTrue("Dispose should signal OnCloseRequested");
        }

        [Test]
        public void WhenDisposeCalledMultipleTimes_ThenNoExceptionThrown()
        {
            // Arrange & Act
            Action disposeMultipleTimes = () =>
            {
                _viewModel.Dispose();
                _viewModel.Dispose();
                _viewModel.Dispose();
            };

            // Assert
            disposeMultipleTimes.Should().NotThrow("Dispose should be idempotent");
        }

        #endregion

        #region AddDisposable Tests

        [Test]
        public void WhenAddDisposableCalled_ThenDisposableIsTracked()
        {
            // Arrange
            var disposable = new MockDisposable();

            // Act
            _viewModel.AddDisposable(disposable);
            _viewModel.Dispose();

            // Assert
            disposable.DisposeCallCount.Should().Be(1, "added disposable should be tracked and disposed");
        }

        #endregion

        #region RequestClose Tests

        [Test]
        public void WhenRequestCloseCalled_ThenOnCloseRequestedEmitsEvent()
        {
            // Arrange
            var eventCount = 0;
            _viewModel.OnCloseRequested.Subscribe(_ => eventCount++);

            // Act
            _viewModel.RequestClose();

            // Assert
            eventCount.Should().Be(1, "OnCloseRequested should emit exactly one event");
        }

        [Test]
        public void WhenRequestCloseCalledAfterDispose_ThenThrowsDisposedException()
        {
            // Arrange
            var eventCount = 0;
            _viewModel.OnCloseRequested.Subscribe(_ => eventCount++);
            _viewModel.Dispose(); // Emits 1 event

            // Act
            Action requestCloseAfterDispose = () => _viewModel.RequestClose();

            // Assert
            requestCloseAfterDispose.Should().Throw<ObjectDisposedException>(
                "RequestClose should throw when called on disposed Subject");

            eventCount.Should().Be(1, "exactly one event (from Dispose) should be emitted, subsequent calls fail");
        }

        #endregion

        #region Integration Tests

        [Test]
        public void WhenResetThenDispose_ThenDisposablesAreCleanedOnlyOnce()
        {
            // Arrange
            var disposable = new MockDisposable();
            _viewModel.AddDisposable(disposable);

            // Act
            _viewModel.Reset();
            _viewModel.Dispose();

            // Assert
            disposable.DisposeCallCount.Should().Be(1, 
                "disposable should be disposed only once during Reset, not again during Dispose");
        }

        #endregion

        #region Test Helpers

        private class TestViewModel : BaseViewModel
        {
            public bool WasOnResetCalled { get; private set; }
            public bool WasOnDisposeCalled { get; private set; }

            public new void AddDisposable(IDisposable disposable) => base.AddDisposable(disposable);
            
            public new void RequestClose() => base.RequestClose();

            protected override void OnReset() => WasOnResetCalled = true;

            protected override void OnDispose() => WasOnDisposeCalled = true;

            public void ResetFlags()
            {
                WasOnResetCalled = false;
                WasOnDisposeCalled = false;
            }
        }

        private class MockDisposable : IDisposable
        {
            public int DisposeCallCount { get; private set; }

            public void Dispose() => DisposeCallCount++;
        }

        #endregion
    }
}