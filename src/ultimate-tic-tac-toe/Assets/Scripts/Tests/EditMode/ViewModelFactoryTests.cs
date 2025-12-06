using System;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using Runtime.Services.UI;
using Runtime.UI.Core;
using VContainer;

namespace Tests.EditMode
{
    [TestFixture]
    public class ViewModelFactoryTests
    {
        private IObjectResolver _mockContainer;
        private ViewModelFactory _factory;

        [SetUp]
        public void SetUp()
        {
            _mockContainer = Substitute.For<IObjectResolver>();
            _factory = new ViewModelFactory(_mockContainer);
        }

        #region Resolve from DI Container Tests

        [Test]
        public void WhenViewModelRegisteredInContainer_ThenReturnsRegisteredInstance()
        {
            // Arrange
            var registeredViewModel = new TestViewModelWithoutDeps();
            
            _mockContainer.Resolve(typeof(TestViewModelWithoutDeps))
                .Returns(registeredViewModel);

            // Act
            var result = _factory.CreateViewModel<TestViewModelWithoutDeps>();

            // Assert
            result.Should().BeSameAs(registeredViewModel, 
                "factory should return the exact instance from DI container");
        }

        [Test]
        public void WhenViewModelNotInContainer_ThenCreatesManually()
        {
            // Arrange
            _mockContainer.Resolve(typeof(TestViewModelWithoutDeps))
                .Returns(_ => throw new Exception("Type not registered"));

            // Act
            var result = _factory.CreateViewModel<TestViewModelWithoutDeps>();

            // Assert
            result.Should().NotBeNull("factory should create instance manually");
            
            result.Should().BeOfType<TestViewModelWithoutDeps>(
                "factory should create correct type");
        }

        #endregion

        #region Creation with Dependencies Tests

        [Test]
        public void WhenViewModelHasDependencies_ThenDependenciesResolved()
        {
            // Arrange
            var mockService = Substitute.For<ITestService>();
            
            _mockContainer.Resolve(typeof(TestViewModelWithDeps))
                .Returns(_ => throw new Exception("Type not registered"));
            
            _mockContainer.Resolve(typeof(ITestService))
                .Returns(mockService);

            // Act
            var result = _factory.CreateViewModel<TestViewModelWithDeps>();

            // Assert
            result.Should().NotBeNull("factory should create ViewModel");
            
            result.Service.Should().BeSameAs(mockService, 
                "dependency should be resolved from container");
            
            _mockContainer.Received(1).Resolve(typeof(ITestService));
        }

        [Test]
        public void WhenViewModelHasMultipleDependencies_ThenAllResolved()
        {
            // Arrange
            var mockService1 = Substitute.For<ITestService>();
            var mockService2 = Substitute.For<ITestService2>();
            
            _mockContainer.Resolve(typeof(TestViewModelMultipleDeps))
                .Returns(_ => throw new Exception("Type not registered"));
            
            _mockContainer.Resolve(typeof(ITestService))
                .Returns(mockService1);
            
            _mockContainer.Resolve(typeof(ITestService2))
                .Returns(mockService2);

            // Act
            var result = _factory.CreateViewModel<TestViewModelMultipleDeps>();

            // Assert
            result.Should().NotBeNull("factory should create ViewModel");
            
            result.Service1.Should().BeSameAs(mockService1, 
                "first dependency should be resolved");
            
            result.Service2.Should().BeSameAs(mockService2, 
                "second dependency should be resolved");
            
            _mockContainer.Received(1).Resolve(typeof(ITestService));
            _mockContainer.Received(1).Resolve(typeof(ITestService2));
        }

        #endregion

        #region Test Fixtures

        private class TestViewModelWithoutDeps : BaseViewModel { }

        public interface ITestService { }

        private class TestViewModelWithDeps : BaseViewModel
        {
            public ITestService Service { get; }
            
            public TestViewModelWithDeps(ITestService service) => Service = service;
        }

        public interface ITestService2 { }

        private class TestViewModelMultipleDeps : BaseViewModel
        {
            public ITestService Service1 { get; }
            public ITestService2 Service2 { get; }
            
            public TestViewModelMultipleDeps(ITestService service1, ITestService2 service2)
            {
                Service1 = service1;
                Service2 = service2;
            }
        }

        #endregion
    }
}

