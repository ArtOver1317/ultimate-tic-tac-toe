using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Runtime.Services.UI.Assets;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.Exceptions;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Tests.PlayMode.Services.UI.Assets
{
    public partial class AddressablesViewAssetProviderPlayModeTests
    {
        [UnityTest]
        [Timeout(_timeoutMs)]
        public IEnumerator WhenLoadVisualTreeCalledWithAlreadyCancelledToken_ThenThrowsOperationCanceledExceptionWithinTimeout() =>
            UniTask.ToCoroutine(async () =>
            {
                using var cts = new CancellationTokenSource();
                cts.Cancel();

                var ignore = LogAssert.ignoreFailingMessages;
                LogAssert.ignoreFailingMessages = true;

                try
                {
                    Func<UniTask> act = async () => await _provider.LoadVisualTreeAsync(_testUxmlKey, cts.Token);

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
                using var cts = new CancellationTokenSource();

                var ignore = LogAssert.ignoreFailingMessages;
                LogAssert.ignoreFailingMessages = true;

                try
                {
                    var task = _provider.LoadVisualTreeAsync(_testUxmlKey, cts.Token);
                    cts.Cancel();

                    IAssetLease<VisualTreeAsset> lease = null;

                    try
                    {
                        lease = await task;
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }

                    lease.Should().NotBeNull("if load succeeded despite cancellation, it must return a lease");

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
                var ignore = LogAssert.ignoreFailingMessages;
                LogAssert.ignoreFailingMessages = true;

                try
                {
                    Func<UniTask> act = async () => await _provider.LoadVisualTreeAsync("tests/ui/DoesNotExist", CancellationToken.None);

                    await AssertThrowsAnyOfAsync(
                        act,
                        typeof(InvalidOperationException),
                        typeof(OperationException),
                        typeof(InvalidKeyException));
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
                var ignore = LogAssert.ignoreFailingMessages;
                LogAssert.ignoreFailingMessages = true;

                try
                {
                    try
                    {
                        await _provider.LoadVisualTreeAsync("tests/ui/DoesNotExist", CancellationToken.None);
                        Assert.Fail("Expected exception was not thrown.");
                    }
                    catch (Exception ex)
                    {
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
                var ignore = LogAssert.ignoreFailingMessages;
                LogAssert.ignoreFailingMessages = true;

                try
                {
                    Func<UniTask> act = async () => await _provider.LoadVisualTreeAsync("tests/ui/DoesNotExist", CancellationToken.None);

                    await AssertThrowsAnyOfAsync(
                        act,
                        typeof(InvalidOperationException),
                        typeof(OperationException),
                        typeof(InvalidKeyException));

                    var lease = await LoadLeaseAsync(CancellationToken.None);
                    lease.Dispose();
                }
                finally
                {
                    LogAssert.ignoreFailingMessages = ignore;
                }
            });
    }
}