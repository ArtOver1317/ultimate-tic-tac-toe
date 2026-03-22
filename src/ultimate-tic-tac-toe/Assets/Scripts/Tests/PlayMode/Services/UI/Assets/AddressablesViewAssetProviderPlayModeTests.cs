using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Runtime.Services.UI.Assets;
using UnityEngine.AddressableAssets;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AddressableAssets;
#endif

namespace Tests.PlayMode.Services.UI.Assets
{
    [Category("Integration")]
    [TestFixture]
    public partial class AddressablesViewAssetProviderPlayModeTests
    {
        private const int _timeoutMs = 10000;

        private const string _testAssetsFolderPath = "Assets/Tests/AddressablesTestAssets";
        private const string _testUxmlPath = _testAssetsFolderPath + "/TestViewAssetProvider.uxml";
        private const string _testUxmlKey = "TestViewAssetProvider";

        private AddressablesViewAssetProvider _provider;
        private readonly List<IAssetLease<VisualTreeAsset>> _leasesToDispose = new();

#if UNITY_EDITOR
        private string _testUxmlGuid;
        private bool _createdUxmlAsset;
        private bool _createdAddressablesEntry;
        private string _previousEntryAddress;
        private bool _didChangeEntryAddress;
#endif

#if UNITY_EDITOR
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _createdUxmlAsset = false;
            _createdAddressablesEntry = false;
            _previousEntryAddress = null;
            _didChangeEntryAddress = false;

            if (!AssetDatabase.IsValidFolder("Assets/Tests"))
                AssetDatabase.CreateFolder("Assets", "Tests");

            if (!AssetDatabase.IsValidFolder(_testAssetsFolderPath))
                AssetDatabase.CreateFolder("Assets/Tests", "AddressablesTestAssets");

            if (!File.Exists(_testUxmlPath))
            {
                File.WriteAllText(
                    _testUxmlPath,
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
                    "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\">\n" +
                    "    <ui:VisualElement name=\"Root\" />\n" +
                    "</ui:UXML>\n");

                AssetDatabase.ImportAsset(_testUxmlPath, ImportAssetOptions.ForceSynchronousImport);
                _createdUxmlAsset = true;
            }

            _testUxmlGuid = AssetDatabase.AssetPathToGUID(_testUxmlPath);
            _testUxmlGuid.Should().NotBeNullOrWhiteSpace("test UXML asset must have a GUID");

            var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
            var group = settings.DefaultGroup;
            var didChangeSettings = false;

            var existingEntry = settings.FindAssetEntry(_testUxmlGuid);
            
            if (existingEntry != null)
            {
                if (!string.Equals(existingEntry.address, _testUxmlKey, StringComparison.Ordinal))
                {
                    _previousEntryAddress = existingEntry.address;
                    existingEntry.address = _testUxmlKey;
                    _didChangeEntryAddress = true;
                    didChangeSettings = true;
                }
            }
            else
            {
                var entry = settings.CreateOrMoveEntry(_testUxmlGuid, group);
                entry.address = _testUxmlKey;
                _createdAddressablesEntry = true;
                didChangeSettings = true;
            }

            if (didChangeSettings)
            {
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
            }
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            if (string.IsNullOrWhiteSpace(_testUxmlGuid))
                Assert.Inconclusive("Test UXML GUID was not initialized; cannot guarantee Addressables teardown.");

            try
            {
                var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
               
                if (settings == null)
                    Assert.Fail("AddressableAssetSettings is null during OneTimeTearDown; cannot guarantee revert of test changes.");

                var didChangeSettings = false;

                var entry = settings.FindAssetEntry(_testUxmlGuid);
              
                if (entry != null)
                {
                    if (_createdAddressablesEntry)
                    {
                        settings.RemoveAssetEntry(_testUxmlGuid);
                        didChangeSettings = true;
                    }
                    else if (_didChangeEntryAddress)
                    {
                        entry.address = _previousEntryAddress;
                        didChangeSettings = true;
                    }
                }

                if (_createdUxmlAsset && File.Exists(_testUxmlPath)) 
                    AssetDatabase.DeleteAsset(_testUxmlPath);

                if (didChangeSettings)
                {
                    EditorUtility.SetDirty(settings);
                    AssetDatabase.SaveAssets();
                }
            }
            catch (Exception ex)
            {
                Assert.Fail($"OneTimeTearDown failed to revert Addressables changes: {ex.GetType().Name}: {ex.Message}");
            }
        }
#endif

        [UnitySetUp]
        public IEnumerator SetUp()
        {
#if !UNITY_EDITOR
            Assert.Ignore("AddressablesViewAssetProvider integration tests require UNITY_EDITOR (AssetDatabase-backed test setup). ");
#endif

            yield return Addressables.InitializeAsync();

            _provider = new AddressablesViewAssetProvider();
            _leasesToDispose.Clear();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (var lease in _leasesToDispose)
            {
                try
                {
                    lease?.Dispose();
                }
                catch
                {
                    // Best-effort cleanup in tear down
                }
            }

            _leasesToDispose.Clear();
            _provider = null;
            yield return null;
        }

        private async UniTask<IAssetLease<VisualTreeAsset>> LoadLeaseAsync(CancellationToken ct)
        {
            var lease = await _provider.LoadVisualTreeAsync(_testUxmlKey, ct);
            _leasesToDispose.Add(lease);
            return lease;
        }

        private static async UniTask AssertThrowsAnyOfAsync(Func<UniTask> act, params Type[] expectedExceptionTypes)
        {
            try
            {
                await act();
                Assert.Fail("Expected exception was not thrown.");
            }
            catch (Exception ex)
            {
                if (ContainsAnyExpectedException(ex, expectedExceptionTypes))
                    return;

                Assert.Fail($"Unexpected exception type: {ex.GetType().Name}");
            }
        }

        private static async UniTask AssertThrowsOperationCanceledAsync(Func<UniTask> act)
        {
            try
            {
                await act();
                Assert.Fail("Expected OperationCanceledException was not thrown.");
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
        }

        private static async UniTask AssertThrowsOperationCanceledOrWrappedAsync(Func<UniTask> act)
        {
            try
            {
                await act();
                Assert.Fail("Expected OperationCanceledException was not thrown.");
            }
            catch (Exception ex)
            {
                if (ContainsAnyExpectedException(ex, new[] { typeof(OperationCanceledException) }))
                    return;

                Assert.Fail($"Unexpected exception type: {ex.GetType().Name}");
            }
        }

        private static async UniTask AssertThrowsInvalidOperationOrWrappedAsync(Func<UniTask> act)
        {
            try
            {
                await act();
                Assert.Fail("Expected InvalidOperationException was not thrown.");
            }
            catch (Exception ex)
            {
                if (ContainsAnyExpectedException(ex, new[] { typeof(InvalidOperationException) }))
                    return;

                Assert.Fail($"Unexpected exception type: {ex.GetType().Name}");
            }
        }

        private static bool ContainsAnyExpectedException(Exception ex, Type[] expectedExceptionTypes)
        {
            var pending = new Stack<Exception>();
            pending.Push(ex);

            while (pending.Count > 0)
            {
                var current = pending.Pop();

                foreach (var exceptionType in expectedExceptionTypes)
                {
                    if (exceptionType.IsInstanceOfType(current))
                        return true;
                }

                if (current is AggregateException aggregate)
                {
                    foreach (var inner in aggregate.InnerExceptions)
                    {
                        if (inner != null)
                            pending.Push(inner);
                    }
                }

                if (current.InnerException != null)
                    pending.Push(current.InnerException);
            }

            return false;
        }
    }
}
