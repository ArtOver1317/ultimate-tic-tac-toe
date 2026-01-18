using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.UIElements;

namespace Runtime.Services.UI.Assets
{
    /// <summary>
    /// Loads UI Toolkit view assets (e.g. UXML) and returns them as disposable leases.
    /// The provider controls caching policy and release semantics.
    /// </summary>
    public interface IViewAssetProvider
    {
        /// <summary>
        /// Loads a <see cref="VisualTreeAsset"/> by Addressables key.
        /// The returned lease must be disposed to release underlying resources.
        /// </summary>
        /// <remarks>
        /// Threading: implementation marshals Addressables calls to Unity main thread.
        /// Ownership: the returned lease must be disposed on Unity main thread.
        /// </remarks>
        UniTask<IAssetLease<VisualTreeAsset>> LoadVisualTreeAsync(string key, CancellationToken ct);
    }
}
