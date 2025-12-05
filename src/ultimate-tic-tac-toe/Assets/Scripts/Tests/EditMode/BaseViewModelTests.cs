using System;
using FluentAssertions;
using NUnit.Framework;
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

        #endregion

        #region Test Helpers

        private class TestViewModel : BaseViewModel
        {
            public bool WasOnResetCalled { get; private set; }
            public bool WasOnDisposeCalled { get; private set; }

            public new void AddDisposable(IDisposable disposable) => base.AddDisposable(disposable);

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

