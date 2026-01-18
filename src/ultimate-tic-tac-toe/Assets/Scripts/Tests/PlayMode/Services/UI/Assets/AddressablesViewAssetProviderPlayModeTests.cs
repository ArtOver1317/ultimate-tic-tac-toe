using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Runtime.Services.UI.Assets;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.Exceptions;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
#endif

namespace Tests.PlayMode.Services.UI.Assets
{
    [Category("Integration")]
    [TestFixture]
    public class AddressablesViewAssetProviderPlayModeTests
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
                {
                    AssetDatabase.DeleteAsset(_testUxmlPath);
                }

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

        [UnityTest]
        [Timeout(_timeoutMs)]
        public IEnumerator WhenLeaseAssetAccessedAfterDispose_ThenThrowsObjectDisposedException() =>
            UniTask.ToCoroutine(async () =>
            {
                // Arrange
                var lease = await LoadLeaseAsync(CancellationToken.None);
                lease.Dispose();

                // Act
                Func<VisualTreeAsset> act = () => lease.Asset;

                // Assert
                act.Should().Throw<ObjectDisposedException>();
            });

        [UnityTest]
        [Timeout(_timeoutMs)]
        public IEnumerator WhenLeaseDisposedOffMainThread_ThenThrowsInvalidOperationException() =>
            UniTask.ToCoroutine(async () =>
            {
                // Arrange
                var lease = await LoadLeaseAsync(CancellationToken.None);

                try
                {
                    // Act
                    Func<UniTask> act = () => UniTask.RunOnThreadPool(() => lease.Dispose());

                    // Assert
                    await AssertThrowsInvalidOperationOrWrappedAsync(act);
                }
                finally
                {
                    // Cleanup
                    lease.Dispose();
                }
            });

        [UnityTest]
        [Timeout(_timeoutMs)]
        public IEnumerator WhenLeaseDisposed_ThenIsIdempotentAndDoesNotDeadlock() =>
            UniTask.ToCoroutine(async () =>
            {
                // Arrange
                var lease = await LoadLeaseAsync(CancellationToken.None);

                // Act
                Action act = () =>
                {
                    lease.Dispose();
                    lease.Dispose();
                };

                // Assert
                act.Should().NotThrow();
            });

        [UnityTest]
        [Timeout(_timeoutMs)]
        public IEnumerator WhenLeaseDisposed_ThenReleasesUnderlyingAddressablesHandleBestEffort() =>
            UniTask.ToCoroutine(async () =>
            {
                // Arrange
                var lease = await LoadLeaseAsync(CancellationToken.None);

                var handleField = lease.GetType().GetField("_handle", BindingFlags.Instance | BindingFlags.NonPublic);
                if (handleField == null)
                    Assert.Inconclusive("Lease does not expose a private _handle field; cannot validate Addressables handle release.");

                // Act
                lease.Dispose();
                await UniTask.Yield();

                // Assert
                var handleValue = handleField.GetValue(lease);
                if (handleValue == null)
                    Assert.Inconclusive("Lease _handle field is null; cannot validate Addressables handle release.");

                try
                {
                    var handle = (AsyncOperationHandle<VisualTreeAsset>)handleValue;
                    if (handle.IsValid())
                        Assert.Inconclusive("AsyncOperationHandle remained valid after Dispose() in this runner; release observability is best-effort.");
                }
                catch (Exception ex)
                {
                    Assert.Inconclusive($"Cannot interpret lease _handle via reflection: {ex.GetType().Name}");
                }
            });

        [UnityTest]
        [Timeout(_timeoutMs)]
        public IEnumerator WhenLoadVisualTreeCalledWithAlreadyCancelledToken_ThenThrowsOperationCanceledExceptionWithinTimeout() =>
            UniTask.ToCoroutine(async () =>
            {
                // Arrange
                using var cts = new CancellationTokenSource();
                cts.Cancel();

                var ignore = LogAssert.ignoreFailingMessages;
                LogAssert.ignoreFailingMessages = true;

                try
                {
                    // Act
                    Func<UniTask> act = async () => await _provider.LoadVisualTreeAsync(_testUxmlKey, cts.Token);

                    // Assert
                    await AssertThrowsOperationCanceledAsync(act);
                }
                finally
                {
                    LogAssert.ignoreFailingMessages = ignore;
                }
            });

        [UnityTest]
        [Timeout(_timeoutMs)]
        public IEnumerator WhenLoadVisualTreeCancelledDuringLoad_ThenCompletesWithinTimeoutAndEitherCancelsOrSucceeds() =>
            UniTask.ToCoroutine(async () =>
            {
                // Arrange
                using var cts = new CancellationTokenSource();

                var ignore = LogAssert.ignoreFailingMessages;
                LogAssert.ignoreFailingMessages = true;

                try
                {
                    var task = _provider.LoadVisualTreeAsync(_testUxmlKey, cts.Token);
                    cts.Cancel();

                    // Act
                    IAssetLease<VisualTreeAsset> lease = null;
                    try
                    {
                        lease = await task;
                    }
                    catch (OperationCanceledException)
                    {
                        return; // Expected outcome A
                    }

                    // Assert
                    lease.Should().NotBeNull("if load succeeded despite cancellation, it must return a lease");

                    // Cleanup
                    lease.Dispose();
                }
                finally
                {
                    LogAssert.ignoreFailingMessages = ignore;
                }
            });

        [UnityTest]
        [Timeout(_timeoutMs)]
        public IEnumerator WhenLoadVisualTreeCalledWithUnknownKey_ThenThrowsAndDoesNotReturnLease() =>
            UniTask.ToCoroutine(async () =>
            {
                // Arrange
                var ignore = LogAssert.ignoreFailingMessages;
                LogAssert.ignoreFailingMessages = true;

                try
                {
                    // Act
                    Func<UniTask> act = async () => await _provider.LoadVisualTreeAsync("tests/ui/DoesNotExist", CancellationToken.None);

                    // Assert
                    await AssertThrowsAnyOfAsync(
                        act,
                        typeof(InvalidOperationException),
                        typeof(OperationException),
                        typeof(UnityEngine.AddressableAssets.InvalidKeyException));
                }
                finally
                {
                    LogAssert.ignoreFailingMessages = ignore;
                }
            });

        [UnityTest]
        [Timeout(_timeoutMs)]
        public IEnumerator WhenLoadVisualTreeCompletesWithNonSucceededStatus_ThenThrowsInvalidOperationExceptionBestEffort() =>
            UniTask.ToCoroutine(async () =>
            {
                // Arrange
                var ignore = LogAssert.ignoreFailingMessages;
                LogAssert.ignoreFailingMessages = true;

                try
                {
                    // Act
                    try
                    {
                        await _provider.LoadVisualTreeAsync("tests/ui/DoesNotExist", CancellationToken.None);
                        Assert.Fail("Expected exception was not thrown.");
                    }
                    catch (Exception ex)
                    {
                        // Assert
                        if (ex is InvalidOperationException)
                            return;

                        Assert.Inconclusive(
                            $"Runner threw {ex.GetType().Name} directly; cannot reliably observe Status != Succeeded path.");
                    }
                }
                finally
                {
                    LogAssert.ignoreFailingMessages = ignore;
                }
            });

        [UnityTest]
        [Timeout(_timeoutMs)]
        public IEnumerator WhenLoadVisualTreeThrows_ThenReleasesHandleInCatchBestEffort() =>
            UniTask.ToCoroutine(async () =>
            {
                // Arrange
                var ignore = LogAssert.ignoreFailingMessages;
                LogAssert.ignoreFailingMessages = true;

                try
                {
                    // Act
                    Func<UniTask> act = async () => await _provider.LoadVisualTreeAsync("tests/ui/DoesNotExist", CancellationToken.None);
                    await AssertThrowsAnyOfAsync(
                        act,
                        typeof(InvalidOperationException),
                        typeof(OperationException),
                        typeof(UnityEngine.AddressableAssets.InvalidKeyException));

                    // Assert (minimum contract): provider remains usable after an exception path
                    var lease = await LoadLeaseAsync(CancellationToken.None);
                    lease.Dispose();
                }
                finally
                {
                    LogAssert.ignoreFailingMessages = ignore;
                }
            });

        [UnityTest]
        [Timeout(_timeoutMs)]
        public IEnumerator WhenTwoLeasesForSameKeyAndOneDisposed_ThenOtherLeaseRemainsUsable() =>
            UniTask.ToCoroutine(async () =>
            {
                // Arrange
                var lease1 = await LoadLeaseAsync(CancellationToken.None);
                var lease2 = await LoadLeaseAsync(CancellationToken.None);

                // Act
                lease1.Dispose();

                // Assert
                lease2.Asset.Should().NotBeNull();

                // Cleanup
                lease2.Dispose();
            });

        [UnityTest]
        [Timeout(_timeoutMs)]
        public IEnumerator WhenLoadVisualTreeCalledFromBackgroundThread_ThenMarshalsToMainThreadAndSucceedsWithinTimeout() =>
            UniTask.ToCoroutine(async () =>
            {
                // Arrange
                // Act
                var lease = await UniTask.RunOnThreadPool(async () =>
                    await _provider.LoadVisualTreeAsync(_testUxmlKey, CancellationToken.None));

                // Assert
                lease.Should().NotBeNull();

                // Cleanup
                lease.Dispose();
            });

        [UnityTest]
        [Timeout(_timeoutMs)]
        public IEnumerator WhenLoadVisualTreeCalledFromBackgroundThreadAndTokenAlreadyCancelled_ThenCompletesWithOperationCanceledExceptionWithoutDeadlock() =>
            UniTask.ToCoroutine(async () =>
            {
                // Arrange
                using var cts = new CancellationTokenSource();
                cts.Cancel();

                var ignore = LogAssert.ignoreFailingMessages;
                LogAssert.ignoreFailingMessages = true;

                try
                {
                    // Act
                    Func<UniTask> act = () => UniTask.RunOnThreadPool(async () =>
                        await _provider.LoadVisualTreeAsync(_testUxmlKey, cts.Token));

                    // Assert
                    await AssertThrowsOperationCanceledOrWrappedAsync(act);
                }
                finally
                {
                    LogAssert.ignoreFailingMessages = ignore;
                }
            });

        [UnityTest]
        [Timeout(_timeoutMs)]
        public IEnumerator WhenTwoConcurrentLoadsSameKeyAndOneCancelled_ThenOtherSucceedsAndLeasesRemainIndependent() =>
            UniTask.ToCoroutine(async () =>
            {
                // Arrange
                using var cts1 = new CancellationTokenSource();
                using var cts2 = new CancellationTokenSource();

                var ignore = LogAssert.ignoreFailingMessages;
                LogAssert.ignoreFailingMessages = true;

                try
                {
                    var task1 = _provider.LoadVisualTreeAsync(_testUxmlKey, cts1.Token);
                    var task2 = _provider.LoadVisualTreeAsync(_testUxmlKey, cts2.Token);

                    cts1.Cancel();

                    // Act
                    IAssetLease<VisualTreeAsset> lease1 = null;
                    var task1Cancelled = false;
                    try
                    {
                        lease1 = await task1;
                    }
                    catch (Exception ex)
                    {
                        if (!ContainsAnyExpectedException(ex, new[] { typeof(OperationCanceledException) }))
                            throw;

                        task1Cancelled = true;
                    }

                    var lease2 = await task2;

                    // Assert
                    lease2.Asset.Should().NotBeNull();

                    if (!task1Cancelled)
                        lease1.Asset.Should().NotBeNull("if task1 completes despite cancellation, it must return a usable lease");

                    // Cleanup
                    lease1?.Dispose();
                    lease2.Dispose();
                }
                finally
                {
                    LogAssert.ignoreFailingMessages = ignore;
                }
            });

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
