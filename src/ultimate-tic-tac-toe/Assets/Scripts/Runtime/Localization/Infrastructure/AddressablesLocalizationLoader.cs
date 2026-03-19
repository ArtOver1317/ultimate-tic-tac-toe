using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Runtime.Localization
{
    public sealed class AddressablesLocalizationLoader : ILocalizationLoader
    {
        private readonly Dictionary<string, AsyncOperationHandle<TextAsset>> _handles = new();

        public async UniTask<ReadOnlyMemory<byte>> LoadBytesAsync(string assetKey, CancellationToken cancellationToken)
        {
            var key = NormalizeRequiredKey(assetKey);

            var existingLoad = await TryLoadFromExistingHandleAsync(key, cancellationToken);

            if (existingLoad.Found)
                return existingLoad.Bytes;

            await EnsureLocationExistsAsync(key, cancellationToken);

            return await LoadNewAssetBytesAsync(key, cancellationToken);
        }

        public async UniTask PreDownloadAsync(string assetKey, CancellationToken cancellationToken)
        {
            var key = NormalizeRequiredKey(assetKey);

            var handle = Addressables.DownloadDependenciesAsync(key);
            
            try
            {
                await handle.ToUniTask(cancellationToken: cancellationToken);
            }
            finally
            {
                Addressables.Release(handle);
            }
        }

        public void Release(string assetKey)
        {
            if (!TryNormalizeKey(assetKey, out var key))
                return;

            if (!_handles.Remove(key, out var handle))
                return;

            if (handle.IsValid())
                Addressables.Release(handle);
        }

        private async UniTask<(bool Found, ReadOnlyMemory<byte> Bytes)> TryLoadFromExistingHandleAsync(
            string key,
            CancellationToken cancellationToken)
        {
            if (!_handles.TryGetValue(key, out var existingHandle))
                return (false, default);

            if (!existingHandle.IsValid())
            {
                _handles.Remove(key);
                return (false, default);
            }

            if (!existingHandle.IsDone)
                await existingHandle.ToUniTask(cancellationToken: cancellationToken);

            return (true, GetAssetBytesOrThrow(existingHandle.Result, key));
        }

        private static async UniTask EnsureLocationExistsAsync(string key, CancellationToken cancellationToken)
        {
            var locationsHandle = Addressables.LoadResourceLocationsAsync(key, typeof(TextAsset));

            try
            {
                await locationsHandle.ToUniTask(cancellationToken: cancellationToken);

                if (locationsHandle.Status != AsyncOperationStatus.Succeeded ||
                    locationsHandle.Result == null ||
                    locationsHandle.Result.Count == 0)
                    throw new KeyNotFoundException($"No Addressables location found for key '{key}'.");
            }
            finally
            {
                Addressables.Release(locationsHandle);
            }
        }

        private async UniTask<ReadOnlyMemory<byte>> LoadNewAssetBytesAsync(string key, CancellationToken cancellationToken)
        {
            var handle = Addressables.LoadAssetAsync<TextAsset>(key);
            _handles[key] = handle;

            try
            {
                await handle.ToUniTask(cancellationToken: cancellationToken);
                return GetAssetBytesOrThrow(handle.Result, key);
            }
            catch
            {
                // In case of failure, ensure we don't keep a broken handle.
                Release(key);
                throw;
            }
        }

        private static string NormalizeRequiredKey(string assetKey) => 
            string.IsNullOrWhiteSpace(assetKey) 
                ? throw new ArgumentException("Asset key must be non-empty.", nameof(assetKey)) 
                : assetKey.Trim();

        private static bool TryNormalizeKey(string assetKey, out string key)
        {
            if (string.IsNullOrWhiteSpace(assetKey))
            {
                key = string.Empty;
                return false;
            }

            key = assetKey.Trim();
            return true;
        }

        private static ReadOnlyMemory<byte> GetAssetBytesOrThrow(TextAsset asset, string key) =>
            asset == null 
                ? throw new InvalidOperationException($"Addressables returned null TextAsset for '{key}'.") 
                : new ReadOnlyMemory<byte>(asset.bytes);
    }
}