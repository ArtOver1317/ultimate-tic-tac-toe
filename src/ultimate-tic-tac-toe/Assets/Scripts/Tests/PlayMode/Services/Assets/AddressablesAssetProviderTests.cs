using System;
using System.Collections;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Runtime.Services.Assets;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.TestTools;

namespace Tests.PlayMode.Services.Assets
{
    [TestFixture]
    public class AddressablesAssetProviderTests
    {
        private const string _testAssetsFolderPath = "Assets/Tests/AddressablesTestAssets";
        private const string _testPrefabPath = _testAssetsFolderPath + "/TestCube.prefab";

        private AddressablesAssetProvider _provider;
        private AssetReferenceGameObject _prefabReference;

#if UNITY_EDITOR
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Tests"))
                AssetDatabase.CreateFolder("Assets", "Tests");

            if (!AssetDatabase.IsValidFolder(_testAssetsFolderPath))
                AssetDatabase.CreateFolder("Assets/Tests", "AddressablesTestAssets");

            if (!File.Exists(_testPrefabPath))
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = "TestCube";

                try
                {
                    PrefabUtility.SaveAsPrefabAsset(cube, _testPrefabPath);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(cube);
                }
            }

            var guid = AssetDatabase.AssetPathToGUID(_testPrefabPath);
            _prefabReference = new AssetReferenceGameObject(guid);

            var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
            var group = settings.DefaultGroup;
            var entry = settings.CreateOrMoveEntry(guid, group);
            entry.address = "TestCube";

            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
        }
#endif

        [UnitySetUp]
        public IEnumerator Setup()
        {
#if !UNITY_EDITOR
            Assert.Ignore("AddressablesAssetProvider integration tests require UNITY_EDITOR (AssetDatabase-backed test setup). ");
#endif

            yield return Addressables.InitializeAsync();
            
            _provider = new AddressablesAssetProvider();

            if (_prefabReference == null)
                Assert.Fail("Test Addressable AssetReference was not initialized.");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            _provider?.Cleanup();
            _provider?.Dispose();
            _provider = null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator WhenLoadAsync_ThenReturnsAsset() => UniTask.ToCoroutine(async () =>
        {
            var result = await _provider.LoadAsync<GameObject>(_prefabReference, CancellationToken.None);
            result.Should().NotBeNull();
        });

        [UnityTest]
        public IEnumerator WhenInstantiateAsync_ThenReturnsInstance() => UniTask.ToCoroutine(async () =>
        {
            var instance = await _provider.InstantiateAsync(_prefabReference, null, CancellationToken.None);
            instance.Should().NotBeNull();
            instance.activeSelf.Should().BeTrue();
            _provider.ReleaseInstance(instance);
        });

        [UnityTest]
        public IEnumerator WhenReleaseInstance_ThenInstanceIsDestroyed() => UniTask.ToCoroutine(async () =>
        {
            var instance = await _provider.InstantiateAsync(_prefabReference, null, CancellationToken.None);
            _provider.ReleaseInstance(instance);

            await UniTask.Yield();

            (instance == null).Should().BeTrue();
        });

        [UnityTest]
        public IEnumerator WhenCleanupCalledTwice_ThenDoesNotThrow() => UniTask.ToCoroutine(async () =>
        {
            await _provider.LoadAsync<GameObject>(_prefabReference, CancellationToken.None);
            await _provider.InstantiateAsync(_prefabReference, null, CancellationToken.None);

            Action act = () =>
            {
                _provider.Cleanup();
                _provider.Cleanup();
            };

            act.Should().NotThrow();
        });

        [UnityTest]
        public IEnumerator WhenLoadInvalidAsset_ThenThrowsExceptionAndProviderStillUsable() => UniTask.ToCoroutine(async () =>
        {
            var invalidGuid = "ffffffffffffffffffffffffffffffff";
            var invalidRef = new AssetReference(invalidGuid);

            var ignore = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;

            try
            {
                Func<UniTask> act = async () => await _provider.LoadAsync<GameObject>(invalidRef, CancellationToken.None);
                await AssertThrowsAnyExceptionAsync(act);
            }
            finally
            {
                LogAssert.ignoreFailingMessages = ignore;
            }

            var result = await _provider.LoadAsync<GameObject>(_prefabReference, CancellationToken.None);
            result.Should().NotBeNull();
        });

        [UnityTest]
        public IEnumerator WhenLoadAsyncCancelled_ThenThrowsOperationCanceledExceptionAndProviderStillUsable() => UniTask.ToCoroutine(async () =>
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var ignore = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;

            try
            {
                Func<UniTask> act = async () => await _provider.LoadAsync<GameObject>(_prefabReference, cts.Token);
                await AssertThrowsOperationCanceledAsync(act);
            }
            finally
            {
                LogAssert.ignoreFailingMessages = ignore;
            }

            var result = await _provider.LoadAsync<GameObject>(_prefabReference, CancellationToken.None);
            result.Should().NotBeNull();
        });

        private static async UniTask AssertThrowsAnyExceptionAsync(Func<UniTask> act)
        {
            try
            {
                await act();
                Assert.Fail("Expected exception was not thrown.");
            }
            catch (Exception)
            {
                // Expected
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
    }
}
