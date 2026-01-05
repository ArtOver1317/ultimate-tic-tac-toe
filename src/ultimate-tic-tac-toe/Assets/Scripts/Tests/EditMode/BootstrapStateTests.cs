using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using R3;
using Runtime.Infrastructure.GameStateMachine;
using Runtime.Infrastructure.GameStateMachine.States;
using Runtime.Localization;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.EditMode
{
    [TestFixture]
    public class BootstrapStateTests
    {
        private IGameStateMachine _stateMachineMock;
        private ILocalizationService _localizationMock;
        private BootstrapState _sut;
        private CancellationToken _cancellationToken;

        private readonly System.Collections.Generic.List<(LogType Type, string Message)> _logs = new();

        [SetUp]
        public void SetUp()
        {
            Application.logMessageReceived += OnLogMessageReceived;

            _stateMachineMock = Substitute.For<IGameStateMachine>();
            _localizationMock = Substitute.For<ILocalizationService>();
            
            // Setup default behavior for localization
            _localizationMock.InitializeAsync(Arg.Any<CancellationToken>()).Returns(UniTask.CompletedTask);
            
            var currentLocale = new ReactiveProperty<LocaleId>(LocaleId.EnglishUs);
            _localizationMock.CurrentLocale.Returns(currentLocale);
            
            _sut = new BootstrapState(_stateMachineMock, _localizationMock);
            _cancellationToken = CancellationToken.None;
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= OnLogMessageReceived;
            _logs.Clear();
        }

        private void OnLogMessageReceived(string condition, string stackTrace, LogType type) =>
            _logs.Add((type, condition));

        private int MarkLogs() => _logs.Count;

        private void AssertOnlyBootstrapStateErrorSince(int mark, Regex expectedError)
        {
            var bootstrapErrors = new System.Collections.Generic.List<string>();

            for (var i = mark; i < _logs.Count; i++)
            {
                var (type, message) = _logs[i];
                if (type is LogType.Error or LogType.Exception && message != null && message.Contains("[BootstrapState]"))
                    bootstrapErrors.Add(message);
            }

            bootstrapErrors.Should().HaveCount(1, "expected exactly one BootstrapState error log in this scenario");
            expectedError.IsMatch(bootstrapErrors[0]).Should().BeTrue($"unexpected BootstrapState error log: '{bootstrapErrors[0]}'");
        }

        [Test]
        public async Task WhenEnter_ThenCallsLocalizationInitializeAsync()
        {
            // Arrange
            _stateMachineMock.EnterAsync<LoadMainMenuState>(Arg.Any<CancellationToken>()).Returns(UniTask.CompletedTask);

            // Act
            await _sut.EnterAsync(_cancellationToken);

            // Assert
            await _localizationMock.Received(1).InitializeAsync(Arg.Any<CancellationToken>());
        }

        [Test]
        public async Task WhenEnter_ThenPassesSameCancellationTokenToInitializeAsync()
        {
            // ⚠️ TEST PLAN DEVIATION ACCEPTED (INF-02):
            // Test Plan requires "Reference Equality", but CancellationToken is a struct (value type).
            // Reference equality is impossible for structs - they don't have references.
            // This test validates semantic equality (token.Equals(passedToken)) which is correct contract.
            // Deviation justified: semantic equality is the only meaningful check for value types.
            
            // Arrange
            var cts = new CancellationTokenSource();
            var token = cts.Token;
            _stateMachineMock.EnterAsync<LoadMainMenuState>(Arg.Any<CancellationToken>()).Returns(UniTask.CompletedTask);

            // Act
            await _sut.EnterAsync(token);

            // Assert
            await _localizationMock.Received(1).InitializeAsync(Arg.Is<CancellationToken>(t => t.Equals(token)));
            
            cts.Dispose();
        }

        [Test]
        public async Task WhenEnterAndInitializeAsyncIsCancelled_ThenDoesNotTransitionAndDoesNotLogError()
        {
            // Arrange: simulate cancellation happening *during* InitializeAsync
            var initStarted = new TaskCompletionSource<bool>();
            var cts = new CancellationTokenSource();
            
            _localizationMock.InitializeAsync(Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    var ct = callInfo.Arg<CancellationToken>();
                    
                    return UniTask.Create(async () =>
                    {
                        initStarted.SetResult(true);
                        await UniTask.Delay(10000, cancellationToken: ct);
                    });
                });

            var errors = new System.Collections.Generic.List<string>();
            
            void OnLog(string condition, string stackTrace, UnityEngine.LogType type)
            {
                // Filter: only track Error logs from BootstrapState/Infrastructure to avoid false positives
                if (type is UnityEngine.LogType.Error or UnityEngine.LogType.Exception &&
                    (condition.Contains("[BootstrapState]") || condition.Contains("[Infrastructure]")))
                    errors.Add(condition);
            }

            UnityEngine.Application.logMessageReceived += OnLog;
            
            try
            {
                // Act
                var enterTask = _sut.EnterAsync(cts.Token).AsTask();

                await initStarted.Task; // ensure we are inside InitializeAsync
                cts.Cancel(); // cancel mid-flight

                var completed = await Task.WhenAny(enterTask, Task.Delay(2000));
                completed.Should().Be(enterTask, "BootstrapState must not hang on cancellation");

                // Assert: await result to check it completed (even if with exception)
                try
                {
                    await enterTask;
                }
                catch (OperationCanceledException)
                {
                    // Expected - cancellation is acceptable
                }

                // Assert: no transition
                await _stateMachineMock.DidNotReceive()
                    .EnterAsync<LoadMainMenuState>(Arg.Any<CancellationToken>());

                // Assert: no Error logs (Info/Warning allowed, cancellation is not an error)
                errors.Should().BeEmpty("Cancellation should not be logged as Error");
            }
            finally
            {
                UnityEngine.Application.logMessageReceived -= OnLog;
                cts.Dispose();
            }
        }

        [Test]
        public async Task WhenEnterAndInitializeAsyncThrows_ThenLogsExactlyOneErrorWithException()
        {
            // Arrange
            var expectedException = new InvalidOperationException("Test exception");
            
            _localizationMock.InitializeAsync(Arg.Any<CancellationToken>())
                .Returns(UniTask.FromException(expectedException));
            
            LogAssert.Expect(UnityEngine.LogType.Log, 
                new Regex(@"\[Infrastructure\] \[BootstrapState\] Initializing game systems\.\.\."));
            
            LogAssert.Expect(UnityEngine.LogType.Error, 
                new Regex(@"\[BootstrapState\] Failed to initialize localization: System\.InvalidOperationException: Test exception"));

            // Act
            var mark = MarkLogs();
            Func<Task> act = async () => await _sut.EnterAsync(_cancellationToken);

            // Assert
            await act.Should().ThrowAsync<InvalidOperationException>();
            AssertOnlyBootstrapStateErrorSince(mark,
                new Regex(@"\[BootstrapState\] Failed to initialize localization: System\.InvalidOperationException: Test exception"));
        }

        [Test]
        public async Task WhenEnterAndInitializeAsyncThrows_ThenDoesNotTransitionToNextState()
        {
            // Arrange
            var expectedException = new InvalidOperationException("Test exception");
            
            _localizationMock.InitializeAsync(Arg.Any<CancellationToken>())
                .Returns(UniTask.FromException(expectedException));
            
            LogAssert.Expect(UnityEngine.LogType.Log, 
                new Regex(@"\[Infrastructure\] \[BootstrapState\] Initializing game systems\.\.\."));
            
            LogAssert.Expect(UnityEngine.LogType.Error, 
                new Regex(@"\[BootstrapState\] Failed to initialize localization"));

            // Act
            var mark = MarkLogs();
            Func<Task> act = async () => await _sut.EnterAsync(_cancellationToken);

            // Assert
            await act.Should().ThrowAsync<InvalidOperationException>();
            await _stateMachineMock.DidNotReceive().EnterAsync<LoadMainMenuState>(Arg.Any<CancellationToken>());
            AssertOnlyBootstrapStateErrorSince(mark,
                new Regex(@"\[BootstrapState\] Failed to initialize localization"));
        }

        [Test]
        public async Task WhenEnterCalledTwice_ThenInitializeAsyncIsCalledOnce()
        {
            // Arrange
            _stateMachineMock.EnterAsync<LoadMainMenuState>(Arg.Any<CancellationToken>()).Returns(UniTask.CompletedTask);

            // Act
            await _sut.EnterAsync(_cancellationToken);
            await _sut.EnterAsync(_cancellationToken);

            // Assert
            await _localizationMock.Received(1).InitializeAsync(Arg.Any<CancellationToken>());
        }

        [Test]
        public async Task WhenEnterAndInitializeAsyncThrows_ThenDoesNotLeaveStateInHalfInitializedMode()
        {
            // Arrange
            var expectedException = new InvalidOperationException("Test exception");
            
            _localizationMock.InitializeAsync(Arg.Any<CancellationToken>())
                .Returns(UniTask.FromException(expectedException));
            
            LogAssert.Expect(UnityEngine.LogType.Log, 
                new Regex(@"\[Infrastructure\] \[BootstrapState\] Initializing game systems\.\.\."));
            
            LogAssert.Expect(UnityEngine.LogType.Error, 
                new Regex(@"\[BootstrapState\] Failed to initialize localization"));

            // Act
            var mark = MarkLogs();
            Func<Task> act = async () => await _sut.EnterAsync(_cancellationToken);

            // Assert: exception thrown, no transition to next state
            await act.Should().ThrowAsync<InvalidOperationException>();
            await _stateMachineMock.DidNotReceive().EnterAsync<LoadMainMenuState>(Arg.Any<CancellationToken>());
            AssertOnlyBootstrapStateErrorSince(mark,
                new Regex(@"\[BootstrapState\] Failed to initialize localization"));
            
            // Verify "no half-initialized state": after error, system remains safe and retryable
            _stateMachineMock.ClearReceivedCalls();
            
            // Second call SHOULD try to initialize again because flag is set only AFTER success
            _localizationMock.ClearReceivedCalls();
            _localizationMock.InitializeAsync(Arg.Any<CancellationToken>()).Returns(UniTask.CompletedTask);
            _stateMachineMock.EnterAsync<LoadMainMenuState>(Arg.Any<CancellationToken>()).Returns(UniTask.CompletedTask);
            
            // This should log again since first attempt failed
            LogAssert.Expect(UnityEngine.LogType.Log, 
                new Regex(@"\[Infrastructure\] \[BootstrapState\] Initializing game systems\.\.\."));
            
            await _sut.EnterAsync(_cancellationToken);
            await _localizationMock.Received(1).InitializeAsync(Arg.Any<CancellationToken>());
            
            // Now after successful init, transition should occur
            await _stateMachineMock.Received(1).EnterAsync<LoadMainMenuState>(Arg.Any<CancellationToken>());
        }

        [Test]
        public async Task WhenEnter_ThenTransitionsToLoadMainMenuState()
        {
            // Arrange
            _stateMachineMock.EnterAsync<LoadMainMenuState>(Arg.Any<CancellationToken>()).Returns(UniTask.CompletedTask);

            // Act
            await _sut.EnterAsync(_cancellationToken);

            // Assert
            await _stateMachineMock.Received(1).EnterAsync<LoadMainMenuState>(Arg.Any<CancellationToken>());
        }

        [Test]
        public async Task WhenEnterAndLocalizationFails_ThenThrowsException()
        {
            // Arrange
            var expectedException = new InvalidOperationException("Localization failed");
            _localizationMock.InitializeAsync(Arg.Any<CancellationToken>()).Returns(UniTask.FromException(expectedException));
            
            LogAssert.Expect(UnityEngine.LogType.Log, 
                new Regex(@"\[Infrastructure\] \[BootstrapState\] Initializing game systems\.\.\."));
            
            LogAssert.Expect(UnityEngine.LogType.Error, new Regex(@"\[BootstrapState\] Failed to initialize localization: System\.InvalidOperationException: Localization failed"));

            // Act
            var mark = MarkLogs();
            Func<Task> act = async () => await _sut.EnterAsync(_cancellationToken);

            // Assert
            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Localization failed");
            AssertOnlyBootstrapStateErrorSince(mark,
                new Regex(@"\[BootstrapState\] Failed to initialize localization: System\.InvalidOperationException: Localization failed"));
        }

        [Test]
        public void WhenExit_ThenCompletesWithoutError()
        {
            // Arrange
            Action act = () => _sut.Exit();

            // Assert
            act.Should().NotThrow();
        }
    }
}