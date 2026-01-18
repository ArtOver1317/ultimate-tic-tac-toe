using System;

namespace Runtime.Services.UI.Assets
{
    /// <summary>
    /// Disposable ownership wrapper for a loaded asset.
    /// Disposing the lease must release any underlying load handle.
    /// </summary>
    /// <typeparam name="T">Asset type.</typeparam>
    public interface IAssetLease<out T> : IDisposable
    {
        /// <summary>
        /// Loaded asset instance.
        /// </summary>
        T Asset { get; }
    }
}
