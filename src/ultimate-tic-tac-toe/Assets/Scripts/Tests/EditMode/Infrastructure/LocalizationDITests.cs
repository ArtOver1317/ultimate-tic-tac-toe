using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using Runtime.Localization;
using Runtime.Services.UI;
using VContainer;

namespace Tests.EditMode.Infrastructure
{
    /// <summary>
    /// Test ViewModel that publicly exposes ILocalizationService dependency.
    /// Used for DI tests to avoid reflection on private fields (overspecification).
    /// </summary>
    internal sealed class ProbeLocalizationVm : Runtime.UI.Core.BaseViewModel
    {
        public ILocalizationService Localization { get; }

        public ProbeLocalizationVm(ILocalizationService localization)
            => Localization = localization ?? throw new System.ArgumentNullException(nameof(localization));

        public override void Initialize() { }
    }

    /// <summary>
    /// Unit tests for localization DI abstractions using local test container.
    /// 
    /// ⚠️ IMPORTANT LIMITATION:
    /// These tests use a local ContainerBuilder with mocks to verify abstraction registrations.
    /// They do NOT test the real GameLifetimeScope configuration.
    /// 
    /// ⚠️ SCOPE: EditMode smoke tests only - validates DI abstractions work in isolation.
    /// For AUTHORITATIVE Test Plan DI-01..05 coverage, see PlayMode/Infrastructure/GameLifetimeScopeDITests.cs
    /// 
    /// Current tests validate:
    /// - DI abstractions work correctly (interfaces resolve)
    /// - Singleton lifetime behavior (same instance across resolves)
    /// - Dependency graph resolution (no circular dependencies)
    /// - ViewModelFactory production flow (without explicit VM registration)
    /// 
    /// This suite is fast EditMode smoke test. PlayMode integration tests are the source of truth.
    /// </summary>
    [TestFixture]
    public class LocalizationDiTests
    {
        private IObjectResolver _container;

        [SetUp]
        public void SetUp()
        {
            var builder = new ContainerBuilder();
            
            // Use mocks for dependencies to avoid complex setup
            // This tests the DI configuration, not the implementations
            var policyMock = Substitute.For<ILocalizationPolicy>();
            var storageMock = Substitute.For<ILocaleStorage>();
            var catalogMock = Substitute.For<ILocalizationCatalog>();
            var loaderMock = Substitute.For<ILocalizationLoader>();
            var parserMock = Substitute.For<ILocalizationParser>();
            var storeMock = Substitute.For<ILocalizationStore>();
            var formatterMock = Substitute.For<ITextFormatter>();
            
            builder.RegisterInstance(policyMock).As<ILocalizationPolicy>();
            builder.RegisterInstance(storageMock).As<ILocaleStorage>();
            builder.RegisterInstance(catalogMock).As<ILocalizationCatalog>();
            builder.RegisterInstance(loaderMock).As<ILocalizationLoader>();
            builder.RegisterInstance(parserMock).As<ILocalizationParser>();
            builder.RegisterInstance(storeMock).As<ILocalizationStore>();
            builder.RegisterInstance(formatterMock).As<ITextFormatter>();
            
            // Register real LocalizationService as Singleton
            builder.Register<ILocalizationService, LocalizationService>(Lifetime.Singleton);
            
            // Register ViewModelFactory (production mechanism for creating ViewModels)
            // ⚠️ NOTE: ViewModels are NOT registered explicitly - factory creates them via reflection
            builder.Register<ViewModelFactory>(Lifetime.Singleton);

            _container = builder.Build();
        }

        [TearDown]
        public void TearDown() => _container?.Dispose();

        [Test]
        public void WhenLocalTestContainerBuilt_ThenLocalizationServiceIsSingletonSameInstanceAcrossResolves()
        {
            // Act
            var instance1 = _container.Resolve<ILocalizationService>();
            var instance2 = _container.Resolve<ILocalizationService>();

            // Assert
            instance1.Should().BeSameAs(instance2, "LocalizationService must be registered as Singleton");
        }

        [Test]
        public void WhenLocalTestContainerBuilt_ThenAllLocalizationDependenciesResolvable()
        {
            // Act & Assert
            var policy = _container.Resolve<ILocalizationPolicy>();
            policy.Should().NotBeNull("ILocalizationPolicy must be resolvable");

            var storage = _container.Resolve<ILocaleStorage>();
            storage.Should().NotBeNull("ILocaleStorage must be resolvable");

            var catalog = _container.Resolve<ILocalizationCatalog>();
            catalog.Should().NotBeNull("ILocalizationCatalog must be resolvable");

            var loader = _container.Resolve<ILocalizationLoader>();
            loader.Should().NotBeNull("ILocalizationLoader must be resolvable");

            var parser = _container.Resolve<ILocalizationParser>();
            parser.Should().NotBeNull("ILocalizationParser must be resolvable");

            var store = _container.Resolve<ILocalizationStore>();
            store.Should().NotBeNull("ILocalizationStore must be resolvable");

            var formatter = _container.Resolve<ITextFormatter>();
            formatter.Should().NotBeNull("ITextFormatter must be resolvable");

            var service = _container.Resolve<ILocalizationService>();
            service.Should().NotBeNull("ILocalizationService must be resolvable");
        }

        [Test]
        public void WhenLocalTestContainerBuilt_ThenFactoryCreatesViewModelWithInjectedLocalizationService()
        {
            // Arrange
            var directResolve = _container.Resolve<ILocalizationService>();
            var factory = _container.Resolve<ViewModelFactory>();

            // Act - create ViewModel through factory (production flow, NO explicit VM registration)
            var vm = factory.CreateViewModel<ProbeLocalizationVm>();

            // Assert - verify factory injected same singleton service
            vm.Should().NotBeNull("ViewModelFactory should create ViewModel with dependencies");
            
            vm.Localization.Should().BeSameAs(directResolve,
                "ViewModelFactory must inject same singleton ILocalizationService");

            vm.Dispose();
        }
    }
}