using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.UIElements;

namespace Runtime.Services.UI.Assets
{
    public sealed class AddressablesViewAssetProvider : IViewAssetProvider
    {
        /// <summary>
        /// Loads UXML (<see cref="VisualTreeAsset"/>) by Addressables key.
        /// Returns the raw asset (no cloning). Call-site (View) is responsible for <c>CloneTree()</c>.
        /// </summary>
        /// <remarks>
        /// Policy: no caching/deduplication in current implementation.
        /// Each call creates a new Addressables load handle and must be paired with <see cref="IDisposable.Dispose"/> on the lease.
        /// </remarks>
        public UniTask<IAssetLease<VisualTreeAsset>> LoadVisualTreeAsync(string key, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Addressables key is null or empty.", nameof(key));

            return LoadVisualTreeInternalAsync(key, ct);
        }

        private static async UniTask<IAssetLease<VisualTreeAsset>> LoadVisualTreeInternalAsync(
            string key,
            CancellationToken ct)
        {
            // Addressables/Unity API are expected to be used on main thread.
            // We switch without ct to guarantee cleanup even if the caller cancels.
            await UniTask.SwitchToMainThread();

            var handle = Addressables.LoadAssetAsync<VisualTreeAsset>(key);

            try
            {
                await handle.ToUniTask(cancellationToken: ct);

                if (handle.Status != AsyncOperationStatus.Succeeded)
                    throw new InvalidOperationException(
                        $"[ViewAssetProvider] VisualTreeAsset '{key}' load failed: {handle.Status}.",
                        handle.OperationException);

                var asset = handle.Result;
                if (asset == null)
                {
                    throw new InvalidOperationException(
                        $"[ViewAssetProvider] VisualTreeAsset '{key}' loaded as null. Status: {handle.Status}.",
                        handle.OperationException);
                }

                return new AddressablesAssetLease<VisualTreeAsset>(handle, asset);
            }
            catch
            {
                await UniTask.SwitchToMainThread();

                if (handle.IsValid())
                    Addressables.Release(handle);

                throw;
            }
        }

        private sealed class AddressablesAssetLease<T> : IAssetLease<T> where T : class
        {
            private readonly AsyncOperationHandle<T> _handle;
            private readonly T _asset;
            private int _isDisposed;

            public AddressablesAssetLease(AsyncOperationHandle<T> handle, T asset)
            {
                _handle = handle;
                _asset = asset ?? throw new ArgumentNullException(nameof(asset));
            }

            public T Asset
            {
                get
                {
                    if (Volatile.Read(ref _isDisposed) != 0)
                        throw new ObjectDisposedException(GetType().Name);

                    return _asset;
                }
            }

            public void Dispose()
            {
                if (!PlayerLoopHelper.IsMainThread)
                    throw new InvalidOperationException(
                        "[ViewAssetProvider] IAssetLease.Dispose must be called on Unity main thread.");

                if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
                    return;

                if (_handle.IsValid())
                    Addressables.Release(_handle);
            }
        }
    }
}
