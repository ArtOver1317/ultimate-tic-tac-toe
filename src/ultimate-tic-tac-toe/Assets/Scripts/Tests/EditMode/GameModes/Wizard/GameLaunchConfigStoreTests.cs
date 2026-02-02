using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Runtime.GameModes.Wizard;

namespace Tests.EditMode.GameModes.Wizard
{
    [TestFixture]
    [Category("Unit")]
    public class GameLaunchConfigStoreTests
    {
        private GameLaunchConfigStore _sut;

        [SetUp]
        public void SetUp() => _sut = new GameLaunchConfigStore();

        [TearDown]
        public void TearDown() => _sut = null;

        private static GameLaunchConfig CreateConfig(int seed) =>
            new GameLaunchConfig(
                gameModeId: $"test-mode-{seed}",
                modeConfig: new ClassicModeConfig(boardSize: 3),
                opponentConfig: new LocalHumanConfig());

        [Test]
        public void WhenConcurrentSetAndTryPeek_ThenNoExceptionsAndReturnedValuesAreValid()
        {
            // Arrange
            const int writers = 10;
            const int readers = 10;
            const int iterations = 2000;

            var writtenIds = new ConcurrentDictionary<string, byte>();
            var peekedIds = new ConcurrentBag<string>();
            var exceptions = new ConcurrentQueue<Exception>();

            using var barrier = new Barrier(writers + readers);

            Task WriterAsync(int writerIndex) => Task.Run(() =>
            {
                try
                {
                    barrier.SignalAndWait();

                    for (var i = 0; i < iterations; i++)
                    {
                        var id = $"writer-{writerIndex}-i-{i}";
                        writtenIds.TryAdd(id, 0);
                        _sut.Set(new GameLaunchConfig(id, new ClassicModeConfig(3), new LocalHumanConfig()));
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Enqueue(ex);
                }
            });

            Task ReaderAsync() => Task.Run(() =>
            {
                try
                {
                    barrier.SignalAndWait();

                    for (var i = 0; i < iterations; i++)
                    {
                        if (_sut.TryPeek(out var config))
                            peekedIds.Add(config.GameModeId);
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Enqueue(ex);
                }
            });

            // Act
            var tasks = Enumerable.Range(0, writers).Select(WriterAsync)
                .Concat(Enumerable.Range(0, readers).Select(_ => ReaderAsync()))
                .ToArray();

            var allCompleted = Task.WaitAll(tasks, TimeSpan.FromSeconds(10));

            allCompleted.Should().BeTrue("concurrency test must not hang");

            // Assert
            exceptions.Should().BeEmpty("store должен быть thread-safe без исключений");

            peekedIds.Should().NotBeEmpty("test должен наблюдать хотя бы несколько успешных TryPeek");

            peekedIds.Should().OnlyContain(id =>
                id != null && writtenIds.ContainsKey(id),
                "каждый успешный TryPeek должен возвращать ранее установленный config");
        }

        [Test]
        public void WhenConcurrentTryConsume_ThenOnlyOneSucceeds()
        {
            // Arrange
            var config = CreateConfig(seed: 1);
            _sut.Set(config);

            const int consumers = 10;
            var results = new bool[consumers];
            var exceptions = new ConcurrentQueue<Exception>();

            using var barrier = new Barrier(consumers);

            Task ConsumerAsync(int i) => Task.Run(() =>
            {
                try
                {
                    barrier.SignalAndWait();
                    results[i] = _sut.TryConsume(out _);
                }
                catch (Exception ex)
                {
                    exceptions.Enqueue(ex);
                }
            });

            // Act
            var tasks = Enumerable.Range(0, consumers).Select(ConsumerAsync).ToArray();
            var allCompleted = Task.WaitAll(tasks, TimeSpan.FromSeconds(5));

            allCompleted.Should().BeTrue("concurrency test must not hang");

            // Assert
            exceptions.Should().BeEmpty();
            results.Count(r => r).Should().Be(1);
        }

        [Test]
        public void WhenClearCalledDuringConcurrentReads_ThenNoExceptionThrown()
        {
            // Arrange
            const int readers = 10;
            const int iterations = 5000;

            var exceptions = new ConcurrentQueue<Exception>();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

            var clearTask = Task.Run(() =>
            {
                try
                {
                    while (!cts.IsCancellationRequested)
                    {
                        _sut.Set(CreateConfig(seed: Environment.TickCount));
                        _sut.Clear();
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Enqueue(ex);
                }
            }, cts.Token);

            Task ReaderAsync() => Task.Run(() =>
            {
                try
                {
                    for (var i = 0; i < iterations; i++)
                    {
                        _sut.TryPeek(out _);
                        _sut.TryConsume(out _);
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Enqueue(ex);
                }
            });

            // Act
            var readTasks = Enumerable.Range(0, readers).Select(_ => ReaderAsync()).ToArray();
            var allCompleted = Task.WaitAll(readTasks, TimeSpan.FromSeconds(5));

            allCompleted.Should().BeTrue("concurrency test must not hang");

            cts.Cancel();
            try { clearTask.Wait(500); } catch { }

            // Assert
            exceptions.Should().BeEmpty("Clear/Peek/Consume при конкуренции не должны бросать исключения");
        }
    }
}
