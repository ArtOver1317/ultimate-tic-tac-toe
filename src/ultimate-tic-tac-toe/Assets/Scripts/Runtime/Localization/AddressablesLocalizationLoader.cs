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
            if (string.IsNullOrWhiteSpace(assetKey))
                throw new ArgumentException("Asset key must be non-empty.", nameof(assetKey));

            var key = assetKey.Trim();

            if (_handles.TryGetValue(key, out var existingHandle))
            {
                if (!existingHandle.IsValid())
                    _handles.Remove(key);
                else
                {
                    if (!existingHandle.IsDone)
                        await existingHandle.ToUniTask(cancellationToken: cancellationToken);

                    var existingAsset = existingHandle.Result;
                    
                    return existingAsset == null 
                        ? throw new InvalidOperationException($"Addressables returned null TextAsset for '{key}'.") 
                        : new ReadOnlyMemory<byte>(existingAsset.bytes);
                }
            }

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

            var handle = Addressables.LoadAssetAsync<TextAsset>(key);
            _handles[key] = handle;

            try
            {
                await handle.ToUniTask(cancellationToken: cancellationToken);

                var asset = handle.Result;
                
                return asset == null 
                    ? throw new InvalidOperationException($"Addressables returned null TextAsset for '{key}'.") 
                    : new ReadOnlyMemory<byte>(asset.bytes);
            }
            catch
            {
                // In case of failure, ensure we don't keep a broken handle.
                Release(key);
                throw;
            }
        }

        public async UniTask PreDownloadAsync(string assetKey, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(assetKey))
                throw new ArgumentException("Asset key must be non-empty.", nameof(assetKey));

            var handle = Addressables.DownloadDependenciesAsync(assetKey.Trim());
            
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
            if (string.IsNullOrWhiteSpace(assetKey))
                return;

            var key = assetKey.Trim();

            if (!_handles.Remove(key, out var handle))
                return;

            if (handle.IsValid()) 
                Addressables.Release(handle);
        }
    }
}