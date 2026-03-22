using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Runtime.Services.UI.Assets;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Tests.PlayMode.Services.UI.Assets
{
    public partial class AddressablesViewAssetProviderPlayModeTests
    {
        [UnityTest]
        [Timeout(_timeoutMs)]
        public IEnumerator WhenLoadVisualTreeCalledFromBackgroundThread_ThenMarshalsToMainThreadAndSucceedsWithinTimeout() =>
            UniTask.ToCoroutine(async () =>
            {
                var lease = await UniTask.RunOnThreadPool(async () =>
                    await _provider.LoadVisualTreeAsync(_testUxmlKey, CancellationToken.None));

                lease.Should().NotBeNull();

                lease.Dispose();
            });

        [UnityTest]
        [Timeout(_timeoutMs)]
        public IEnumerator WhenLoadVisualTreeCalledFromBackgroundThreadAndTokenAlreadyCancelled_ThenCompletesWithOperationCanceledExceptionWithoutDeadlock() =>
            UniTask.ToCoroutine(async () =>
            {
                using var cts = new CancellationTokenSource();
                cts.Cancel();

                var ignore = LogAssert.ignoreFailingMessages;
                LogAssert.ignoreFailingMessages = true;

                try
                {
                    Func<UniTask> act = () => UniTask.RunOnThreadPool(async () =>
                        await _provider.LoadVisualTreeAsync(_testUxmlKey, cts.Token));

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
                using var cts1 = new CancellationTokenSource();
                using var cts2 = new CancellationTokenSource();

                var ignore = LogAssert.ignoreFailingMessages;
                LogAssert.ignoreFailingMessages = true;

                try
                {
                    var task1 = _provider.LoadVisualTreeAsync(_testUxmlKey, cts1.Token);
                    var task2 = _provider.LoadVisualTreeAsync(_testUxmlKey, cts2.Token);

                    cts1.Cancel();

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

                    lease2.Asset.Should().NotBeNull();

                    if (!task1Cancelled)
                        lease1.Asset.Should().NotBeNull("if task1 completes despite cancellation, it must return a usable lease");

                    lease1?.Dispose();
                    lease2.Dispose();
                }
                finally
                {
                    LogAssert.ignoreFailingMessages = ignore;
                }
            });
    }
}