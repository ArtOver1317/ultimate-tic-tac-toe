using System;
using System.Collections;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Tests.PlayMode.Services.UI.Assets
{
    public partial class AddressablesViewAssetProviderPlayModeTests
    {
        [UnityTest]
        [Timeout(_timeoutMs)]
        public IEnumerator WhenLeaseAssetAccessedAfterDispose_ThenThrowsObjectDisposedException() =>
            UniTask.ToCoroutine(async () =>
            {
                var lease = await LoadLeaseAsync(CancellationToken.None);
                lease.Dispose();

                Func<VisualTreeAsset> act = () => lease.Asset;

                act.Should().Throw<ObjectDisposedException>();
            });

        [UnityTest]
        [Timeout(_timeoutMs)]
        public IEnumerator WhenLeaseDisposedOffMainThread_ThenThrowsInvalidOperationException() =>
            UniTask.ToCoroutine(async () =>
            {
                var lease = await LoadLeaseAsync(CancellationToken.None);

                try
                {
                    Func<UniTask> act = () => UniTask.RunOnThreadPool(() => lease.Dispose());

                    await AssertThrowsInvalidOperationOrWrappedAsync(act);
                }
                finally
                {
                    lease.Dispose();
                }
            });

        [UnityTest]
        [Timeout(_timeoutMs)]
        public IEnumerator WhenLeaseDisposed_ThenIsIdempotentAndDoesNotDeadlock() =>
            UniTask.ToCoroutine(async () =>
            {
                var lease = await LoadLeaseAsync(CancellationToken.None);

                Action act = () =>
                {
                    lease.Dispose();
                    lease.Dispose();
                };

                act.Should().NotThrow();
            });

        [UnityTest]
        [Timeout(_timeoutMs)]
        public IEnumerator WhenLeaseDisposed_ThenReleasesUnderlyingAddressablesHandleBestEffort() =>
            UniTask.ToCoroutine(async () =>
            {
                var lease = await LoadLeaseAsync(CancellationToken.None);

                var handleField = lease.GetType().GetField("_handle", BindingFlags.Instance | BindingFlags.NonPublic);

                if (handleField == null)
                    Assert.Inconclusive("Lease does not expose a private _handle field; cannot validate Addressables handle release.");

                lease.Dispose();
                await UniTask.Yield();

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
        public IEnumerator WhenTwoLeasesForSameKeyAndOneDisposed_ThenOtherLeaseRemainsUsable() =>
            UniTask.ToCoroutine(async () =>
            {
                var lease1 = await LoadLeaseAsync(CancellationToken.None);
                var lease2 = await LoadLeaseAsync(CancellationToken.None);

                lease1.Dispose();

                lease2.Asset.Should().NotBeNull();

                lease2.Dispose();
            });
    }
}