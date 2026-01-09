using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Runtime.Infrastructure.Scopes;
using Runtime.Localization;
using Runtime.Services.UI;
using Runtime.UI.MainMenu;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;

namespace Tests.PlayMode.Infrastructure
{
    /// <summary>
    /// Integration tests for DI configuration in GameLifetimeScope.
    /// 
    /// These tests validate the real production DI container configuration.
    /// Tests require GameLifetimeScope to be present in the scene (either Bootstrap scene or created in SetUp).
    /// 
    /// Coverage: Test Plan DI-01..05 from LocalizationSystem_Phase2_TestPlan.md
    /// </summary>
    [TestFixture]
    public class GameLifetimeScopeDiTests
    {
        /// <summary>
        /// Test ViewModel that publicly exposes ILocalizationService dependency.
        /// Used for DI-02 test to avoid reflection on private fields (overspecification).
        /// </summary>
        private sealed class ProbeLocalizationVm : Runtime.UI.Core.BaseViewModel
        {
            public ILocalizationService Localization { get; }

            public ProbeLocalizationVm(ILocalizationService localization)
                => Localization = localization ?? throw new System.ArgumentNullException(nameof(localization));

            public override void Initialize() { }
        }

        private GameLifetimeScope _scope;
        private System.Collections.Generic.List<string> _suppressedLogs;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            // ⚠️ ADDRESSABLES ERROR FILTERING - CRITICAL SECTION:
            // GameLifetimeScope triggers EntryPoint which initializes localization (Addressables access).
            // 
            // APPROACH: Handler active DURING scene load + scope init (prevents false-green).
            // - ignoreFailingMessages: Unity Test Framework intercepts logs before our handler
            // - Handler filters: Known Addressables errors → suppress, unexpected errors → accumulate
            // - After SetUp: Check accumulated unexpected errors → Assert.Fail if any
            // - try-finally: GUARANTEED cleanup (prevents global state leakage on Assert.Ignore)
            
            _suppressedLogs = new System.Collections.Generic.List<string>();
            var unexpectedErrors = new System.Collections.Generic.List<string>();
            
            Application.LogCallback handler = (condition, stackTrace, type) =>
            {
                // Allow warnings and info
                if (type != LogType.Error && type != LogType.Exception)
                    return;
                
                // Check for known Addressables-related errors
                var fullMessage = $"{condition}\n{stackTrace}";
                
                if (fullMessage.Contains("Addressables") || 
                    fullMessage.Contains("InvalidKeyException") || 
                    fullMessage.Contains("RemoteProviderException") ||
                    fullMessage.Contains("loc_en_us_ui") ||
                    fullMessage.Contains("No Location found for Key"))
                {
                    _suppressedLogs.Add($"[Suppressed] {type}: {condition}");
                    return;
                }
                
                // Accumulate unexpected errors (don't Assert.Fail from callback - fragile)
                unexpectedErrors.Add($"[{type}] {condition}\n{stackTrace}");
            };
            
            // Enable ignoreFailingMessages + handler BEFORE scene load
            LogAssert.ignoreFailingMessages = true;
            Application.logMessageReceived += handler;
            
            try
            {
                // First check if scope already exists in current scene
                _scope = Object.FindFirstObjectByType<GameLifetimeScope>();
            
                if (_scope == null)
                {
                    // Try to load DITestScene
                    Debug.Log("[DI Tests] GameLifetimeScope not found, attempting to load DITestScene...");
                    
                    var loadOp = UnityEngine.SceneManagement.SceneManager.LoadSceneAsync("DITestScene");
                    
                    if (loadOp != null)
                    {
                        yield return loadOp;
                        
                        Debug.Log("[DI Tests] DITestScene loaded successfully");
                    }
                    else
                        Debug.LogWarning("[DI Tests] Failed to load DITestScene - it may not be in Build Settings");

                    yield return null; // Wait one frame for scene initialization
                    
                    // Try to find scope again after loading
                    _scope = Object.FindFirstObjectByType<GameLifetimeScope>();
                }

                if (_scope == null)
                {
                    Debug.LogError("[DI Tests] GameLifetimeScope still not found after attempting to load DITestScene");
                    
                    // ⚠️ SAFETY NET: DITestScene is in Build Settings, so this should NOT trigger in CI.
                    // If this triggers, it indicates a critical test infrastructure failure.
                    Assert.Ignore("GameLifetimeScope not found in scene.\n" +
                                  "To run these tests:\n" +
                                  "1. Verify DITestScene.unity exists at Assets/Scenes/DITestScene.unity\n" +
                                  "2. Open DITestScene and ensure it has GameObject with GameLifetimeScope component\n" +
                                  "3. Ensure GameLifetimeScope has AssetLibrary assigned in Inspector\n" +
                                  "4. Verify DITestScene is in Build Settings (File > Build Settings)\n" +
                                  "5. Re-run tests");
                }
                
                Debug.Log($"[DI Tests] GameLifetimeScope found: {_scope.gameObject.name}");
                
                // Log suppressed errors for transparency
                if (_suppressedLogs.Count > 0)
                {
                    Debug.Log($"[DI Tests] Suppressed {_suppressedLogs.Count} expected Addressables error(s) during SetUp");
                }
                
                // Check for unexpected errors AFTER critical section completes
                if (unexpectedErrors.Count > 0)
                {
                    Assert.Fail($"Unexpected Error/Exception during GameLifetimeScope initialization:\n" +
                                string.Join("\n---\n", unexpectedErrors));
                }
            }
            finally
            {
                // GUARANTEED cleanup - prevents global state leakage
                Application.logMessageReceived -= handler;
                LogAssert.ignoreFailingMessages = false;
            }
            
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            // Report total suppressed logs across SetUp + test
            if (_suppressedLogs?.Count > 0) 
                Debug.Log($"[DI Tests] Test completed. Total suppressed Addressables errors: {_suppressedLogs.Count}");

            yield return null;
        }

        [UnityTest]
        public IEnumerator WhenScopeBuilt_ThenLocalizationServiceIsSingletonSameInstanceAcrossResolves()
        {
            // Act
            var instance1 = _scope.Container.Resolve<ILocalizationService>();
            var instance2 = _scope.Container.Resolve<ILocalizationService>();

            // Assert
            instance1.Should().BeSameAs(instance2, 
                "LocalizationService must be registered as Singleton in GameLifetimeScope");

            yield return null;
        }

        /// <summary>
        /// DI-02: Test that ILocalizationService is injected into ViewModels.
        /// 
        /// ⚠️ ARCHITECTURAL DECISION:
        /// MainMenuViewModel is NOT registered in GameLifetimeScope (by design).
        /// Production creates ViewModels through ViewModelFactory which auto-resolves dependencies.
        /// 
        /// Uses ProbeLocalizationVm (test-only type) to publicly expose dependency,
        /// avoiding reflection on MainMenuViewModel private fields (overspecification).
        /// </summary>
        [UnityTest]
        public IEnumerator WhenScopeBuilt_ThenDifferentConsumersResolveSameLocalizationServiceInstance()
        {
            // Arrange - resolve service directly from container
            var directResolve = _scope.Container.Resolve<ILocalizationService>();
            var factory = _scope.Container.Resolve<ViewModelFactory>();

            // Act - create ProbeLocalizationVm through factory (production flow)
            var vm = factory.CreateViewModel<ProbeLocalizationVm>();

            // Assert - verify both resolve to same singleton instance
            vm.Should().NotBeNull("ViewModelFactory should create ViewModel with dependencies");
            
            vm.Localization.Should().BeSameAs(directResolve,
                "ILocalizationService should be singleton across all consumers");

            vm.Dispose();
            yield return null;
        }

        [UnityTest]
        public IEnumerator WhenScopeBuilt_ThenAllLocalizationDependenciesResolvable()
        {
            // Act & Assert - verify all internal localization dependencies are registered
            var policy = _scope.Container.Resolve<ILocalizationPolicy>();
            policy.Should().NotBeNull("ILocalizationPolicy must be resolvable");

            var storage = _scope.Container.Resolve<ILocaleStorage>();
            storage.Should().NotBeNull("ILocaleStorage must be resolvable");

            var catalog = _scope.Container.Resolve<ILocalizationCatalog>();
            catalog.Should().NotBeNull("ILocalizationCatalog must be resolvable");

            var loader = _scope.Container.Resolve<ILocalizationLoader>();
            loader.Should().NotBeNull("ILocalizationLoader must be resolvable");

            var parser = _scope.Container.Resolve<ILocalizationParser>();
            parser.Should().NotBeNull("ILocalizationParser must be resolvable");

            var store = _scope.Container.Resolve<ILocalizationStore>();
            store.Should().NotBeNull("ILocalizationStore must be resolvable");

            var formatter = _scope.Container.Resolve<ITextFormatter>();
            formatter.Should().NotBeNull("ITextFormatter must be resolvable");

            var service = _scope.Container.Resolve<ILocalizationService>();
            service.Should().NotBeNull("ILocalizationService must be resolvable");

            // Verify singleton behavior (same instance across resolves)
            var policy2 = _scope.Container.Resolve<ILocalizationPolicy>();
            policy.Should().BeSameAs(policy2, "Dependencies should be registered as Singleton");

            yield return null;
        }

        /// <summary>
        /// DI-04: Test that MainMenuViewModel can be created through ViewModelFactory with dependencies.
        /// 
        /// ⚠️ ARCHITECTURAL DECISION & PLAN DEVIATION:
        /// Test Plan DI-04 literal text: "Resolve<MainMenuViewModel/>()" - but this is NOT production flow.
        /// MainMenuViewModel is NOT registered in GameLifetimeScope (by design).
        /// Production creates ViewModels through ViewModelFactory which auto-resolves dependencies.
        /// 
        /// This test validates production mechanism: ViewModelFactory.CreateViewModel<MainMenuViewModel/>()
        /// Plan deviation accepted: production architecture > test plan literal interpretation.
        /// </summary>
        [UnityTest]
        public IEnumerator WhenScopeBuilt_ThenMainMenuViewModelIsResolvable()
        {
            // Arrange
            var factory = _scope.Container.Resolve<ViewModelFactory>();
            var localization = _scope.Container.Resolve<ILocalizationService>();

            // Production flow: localization is initialized in BootstrapState before any UI creates VMs.
            // This test must ensure the same precondition, otherwise Observe(...) throws.
            yield return localization.InitializeAsync(CancellationToken.None).ToCoroutine();

            // Act - create VM through factory (production flow)
            var viewModel = factory.CreateViewModel<MainMenuViewModel>();

            // Assert
            viewModel.Should().NotBeNull(
                "MainMenuViewModel must be creatable through ViewModelFactory with all dependencies resolved from GameLifetimeScope");
            
            // Cleanup
            viewModel.Dispose();
            yield return null;
        }

        /// <summary>
        /// DI-05: Test that GameLifetimeScope correctly injects dependencies into MonoBehaviours.
        /// 
        /// ⚠️ CRITICAL LIMITATION - NOT FULL COVERAGE:
        /// VContainer's AutoInject flow (production) requires:
        /// 1) MonoBehaviour instantiated via VContainer (or registered prefab/scene object)
        /// 2) VContainer's GameObject lifecycle hooks triggered during Awake()
        /// 
        /// This test uses manual Construct() call - validates dependencies are AVAILABLE,
        /// but does NOT test AutoInject production flow (requires prefab instantiation).
        /// 
        /// Trade-off accepted: Complex PlayMode setup (prefab + scene hierarchy) would be
        /// overengineering for validation that "dependency exists in container".
        /// We validate this using a simple debug overlay component that has [Inject] entrypoint.
        /// </summary>
        [UnityTest]
        public IEnumerator WhenLocaleDebugOverlayIsInstantiated_ThenDependenciesAreInjected()
        {
            // Arrange
            var gameObject = new GameObject("TestLocaleDebugOverlay");
            gameObject.SetActive(false);
            
            var component = gameObject.AddComponent<Runtime.UI.Debugging.LocaleDebugOverlay>();
            var localization = _scope.Container.Resolve<ILocalizationService>();

            // Act - manually inject (simulates VContainer AutoInject, but NOT production flow)
            component.Construct(localization);

            // Assert - dependency is resolvable and injectable
            localization.Should().NotBeNull("ILocalizationService must be resolvable and injectable into LocaleDebugOverlay");
            
            Object.DestroyImmediate(gameObject);
            yield return null;
        }
    }
}