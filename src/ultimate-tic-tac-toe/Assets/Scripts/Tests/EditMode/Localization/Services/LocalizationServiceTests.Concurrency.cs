using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Tests.EditMode.Localization.Services
{
    public partial class LocalizationServiceTests
    {
        [Test]
        public async Task WhenInitializeAsyncCalledConcurrently_ThenWaitsForFirstToComplete()
        {
            const string json = @"{""locale"":""en-US"",""table"":""UI"",""entries"":{""Test.Key"":""Test Value""}}";
            var bytes = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(json));

            var tcs = new UniTaskCompletionSource<ReadOnlyMemory<byte>>();
            var loadCallCount = 0;

            _mockLoader.LoadBytesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(_ =>
                {
                    loadCallCount++;
                    return tcs.Task;
                });

            var task1 = _service.InitializeAsync(CancellationToken.None);
            var task2 = _service.InitializeAsync(CancellationToken.None);
            var task3 = _service.InitializeAsync(CancellationToken.None);

            await Task.Yield();

            tcs.TrySetResult(bytes);

            await UniTask.WhenAll(task1, task2, task3);

            loadCallCount.Should().Be(1);
        }
    }
}