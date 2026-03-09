#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using R3;
using Runtime.Gameplay;
using Runtime.Gameplay.ECS;
using Runtime.GameModes.Wizard;
using Runtime.GameModes.Wizard.Configs;
using Runtime.Games.TicTacToe.AI;
using Runtime.Games.TicTacToe.Moves;
using UnityEditor;
using UnityEngine.TestTools;

namespace Tests.EditMode.Games.TicTacToe.AI
{
    [TestFixture]
    [Category("Unit")]
    public class BotTurnDriverTests
    {
        // ── Fake match state with controllable event subjects ──

        private sealed class FakeMatchState : IMatchStateProvider
        {
            public Subject<CellChangedEvent> CellChangedSubject { get; } = new();
            public Subject<LastMoveChangedEvent> LastMoveChangedSubject { get; } = new();
            public Subject<CurrentPlayerChangedEvent> CurrentPlayerChangedSubject { get; } = new();
            public Subject<CommandRejectedEvent> CommandRejectedSubject { get; } = new();
            public Subject<RoundFinishedEvent> RoundFinishedSubject { get; } = new();

            public Observable<CellChangedEvent> CellChanged => CellChangedSubject;
            public Observable<LastMoveChangedEvent> LastMoveChanged => LastMoveChangedSubject;
            public Observable<CurrentPlayerChangedEvent> CurrentPlayerChanged => CurrentPlayerChangedSubject;
            public Observable<CommandRejectedEvent> CommandRejected => CommandRejectedSubject;
            public Observable<RoundFinishedEvent> RoundFinished => RoundFinishedSubject;

            public bool IsMatchActive { get; set; } = true;
            public int ActivePlayerSlotValue { get; set; }
            public long CommandSequenceValue { get; set; }
            public CellId? LastMoveValue { get; set; }

            // Track submitted commands
            public List<IGameplayCommand> SubmittedCommands { get; } = new();
            public Action<IGameplayCommand>? OnSubmitCommand { get; set; }

            /// <summary>
            /// When true, SubmitCommand increments CommandSequenceValue (simulates accepted move).
            /// When false, CommandSequenceValue stays (simulates rejection).
            /// </summary>
            public bool AcceptCommands { get; set; } = true;

            public int ActivePlayerSlot => ActivePlayerSlotValue;
            public long CommandSequence => CommandSequenceValue;
            public CellId? LastMove => LastMoveValue;

            private readonly List<CellSnapshot> _cells = new();

            public void SetCells(int boardSize, PlayerMark[] marks)
            {
                _cells.Clear();
                for (var r = 0; r < boardSize; r++)
                {
                    for (var c = 0; c < boardSize; c++)
                    {
                        var mark = marks[r * boardSize + c];
                        var slot = mark switch
                        {
                            PlayerMark.X => 0,
                            PlayerMark.O => 1,
                            _ => -1,
                        };
                        _cells.Add(new CellSnapshot(new CellId(r, c), slot));
                    }
                }
            }

            public int GetCellSlot(CellId cellId) => -1;
            public IReadOnlyList<CellSnapshot> GetAllCells() => _cells;

            public void SubmitCommand(IGameplayCommand command)
            {
                SubmittedCommands.Add(command);
                if (AcceptCommands)
                    CommandSequenceValue++;
                OnSubmitCommand?.Invoke(command);
            }

            public void Dispose() { }
        }

        // ── Test helpers ──

        private FakeMatchState _matchState = null!;
        private IBotDecisionEngine _engine = null!;
        private IBotProfileCatalog _catalog = null!;
        private IClassicWinLengthProvider _winLengthProvider = null!;
        private BotProfile _easyProfile = null!;

        [SetUp]
        public void SetUp()
        {
            _matchState = new FakeMatchState();
            _engine = Substitute.For<IBotDecisionEngine>();
            _catalog = Substitute.For<IBotProfileCatalog>();
            _winLengthProvider = Substitute.For<IClassicWinLengthProvider>();

            _easyProfile = UnityEngine.ScriptableObject.CreateInstance<BotProfile>();

            // Set PreMoveDelay to 0 for fast tests
            using (var so = new SerializedObject(_easyProfile))
            {
                so.FindProperty("PreMoveDelayMs").intValue = 0;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            _catalog.TryGet("easy", out Arg.Any<BotProfile?>())
                .Returns(x =>
                {
                    x[1] = _easyProfile;
                    return true;
                });
            _catalog.TryGet("missing", out Arg.Any<BotProfile?>())
                .Returns(x =>
                {
                    x[1] = null;
                    return false;
                });

            _winLengthProvider.GetWinLength(3).Returns(3);
            _winLengthProvider.GetWinLength(5).Returns(4);
        }

        [TearDown]
        public void TearDown()
        {
            if (_easyProfile != null)
                UnityEngine.Object.DestroyImmediate(_easyProfile);
        }

        private BotTurnDriver CreateDriver() =>
            new(_matchState, _engine, _catalog, _winLengthProvider);

        /// <summary>Waits up to ~2s for SubmittedCommands to reach minCount.</summary>
        private async UniTask WaitForSubmitAsync(int minCount = 1)
        {
            for (var i = 0; i < 200; i++)
            {
                if (_matchState.SubmittedCommands.Count >= minCount) return;
                await UniTask.DelayFrame(1);
            }
        }

        private (UniTaskCompletionSource entered, UniTaskCompletionSource release) ConfigureBarrierEngine(
            bool honorCancellation = true,
            CellId? move = null)
        {
            var entered = new UniTaskCompletionSource();
            var release = new UniTaskCompletionSource();
            var selectedMove = move ?? new CellId(1, 1);

            _engine.ChooseMoveAsync(Arg.Any<BotDecisionRequest>(), Arg.Any<BotProfileData>(),
                    Arg.Any<CancellationToken>())
                .Returns(info =>
                {
                    var ct = info.ArgAt<CancellationToken>(2);
                    return UniTask.Create(async () =>
                    {
                        entered.TrySetResult();

                        if (honorCancellation)
                            await release.Task.AttachExternalCancellation(ct);
                        else
                            await release.Task;

                        return selectedMove;
                    });
                });

            return (entered, release);
        }

        private static async UniTask WaitUntilEngineEnteredAsync(UniTaskCompletionSource entered)
        {
            var winner = await UniTask.WhenAny(entered.Task, UniTask.Delay(2000));
            winner.Should().Be(0, "engine must actually enter ChooseMoveAsync before race mutation");
        }

        private static async UniTask WaitUntilNotBusyAsync(BotTurnDriver driver)
        {
            for (var i = 0; i < 200; i++)
            {
                if (!driver.IsBusy.CurrentValue)
                    return;

                await UniTask.DelayFrame(1);
            }

            driver.IsBusy.CurrentValue.Should().BeFalse("turn should finish within a reasonable number of frames");
        }

        private static GameLaunchConfig MakeConfig(int boardSize = 3, bool isUltimate = false,
            string difficultyId = "easy")
        {
            var gameConfig = new TicTacToeConfig(boardSize, isUltimate);
            var opponentConfig = new BotOpponentConfig(difficultyId);
            return new GameLaunchConfig("tic-tac-toe", gameConfig, opponentConfig);
        }

        private void SetupEmptyBoard(int boardSize = 3)
        {
            var cells = new PlayerMark[boardSize * boardSize];
            _matchState.SetCells(boardSize, cells);
            _matchState.ActivePlayerSlotValue = 1; // bot slot
            _matchState.CommandSequenceValue = 0;
        }

        // ══════════════════════════════════════════════
        //  ADR-2: Classic-only guard
        // ══════════════════════════════════════════════

        [Test]
        public void WhenStartedWithUltimateConfig_ThenReturnsUnsupportedConfig()
        {
            var driver = CreateDriver();
            var config = MakeConfig(isUltimate: true);

            var result = driver.StartAsync(config, 1, "easy", CancellationToken.None).GetAwaiter().GetResult();

            result.Status.Should().Be(BotStartStatus.UnsupportedConfig);
            driver.Dispose();
        }

        [Test]
        public void WhenStartedWithUnknownDifficulty_ThenReturnsFailed()
        {
            var driver = CreateDriver();
            var config = MakeConfig(difficultyId: "missing");

            var result = driver.StartAsync(config, 1, "missing", CancellationToken.None).GetAwaiter().GetResult();

            result.Status.Should().Be(BotStartStatus.Failed);
            result.Error.Should().Contain("missing");
            driver.Dispose();
        }

        [Test]
        public void WhenStartedWithValidConfig_ThenReturnsStarted()
        {
            var driver = CreateDriver();
            SetupEmptyBoard();
            // Make sure engine won't be called during start (bot not first)
            _matchState.ActivePlayerSlotValue = 0; // human's turn

            var result = driver.StartAsync(MakeConfig(), 1, "easy", CancellationToken.None)
                .GetAwaiter().GetResult();

            result.Status.Should().Be(BotStartStatus.Started);
            driver.Dispose();
        }

        [Test]
        public void WhenStartedTwice_ThenSecondCallReturnsFailed()
        {
            var driver = CreateDriver();
            _matchState.ActivePlayerSlotValue = 0;
            SetupEmptyBoard();

            driver.StartAsync(MakeConfig(), 1, "easy", CancellationToken.None).GetAwaiter().GetResult();
            var result = driver.StartAsync(MakeConfig(), 1, "easy", CancellationToken.None)
                .GetAwaiter().GetResult();

            result.Status.Should().Be(BotStartStatus.Failed);
            driver.Dispose();
        }

        // ══════════════════════════════════════════════
        //  ADR-10: Bot turn detection
        // ══════════════════════════════════════════════

        [UnityTest]
        public IEnumerator WhenCurrentPlayerIsBotSlot_ThenSubmitsMoveCommand() => UniTask.ToCoroutine(async () =>
        {
            var driver = CreateDriver();
            SetupEmptyBoard();
            _matchState.ActivePlayerSlotValue = 0; // human first

            var expectedMove = new CellId(1, 1);
            _engine.ChooseMoveAsync(Arg.Any<BotDecisionRequest>(), Arg.Any<BotProfileData>(),
                    Arg.Any<CancellationToken>())
                .Returns(UniTask.FromResult(expectedMove));

            await driver.StartAsync(MakeConfig(), 1, "easy", CancellationToken.None);

            // Simulate human move → now bot's turn
            _matchState.ActivePlayerSlotValue = 1;
            _matchState.CommandSequenceValue = 1;
            _matchState.CurrentPlayerChangedSubject.OnNext(new CurrentPlayerChangedEvent(1));

            // Wait for async turn to complete (yield + compute)
            await WaitForSubmitAsync();

            _matchState.SubmittedCommands.Should().ContainSingle();
            var submitted = _matchState.SubmittedCommands[0];
            submitted.Should().BeAssignableTo<MakeMoveCommand>();
            ((MakeMoveCommand)submitted).CellId.Should().Be(expectedMove);

            driver.Dispose();
        });

        [UnityTest]
        public IEnumerator WhenCurrentPlayerIsNotBotSlot_ThenDoesNotSubmit() => UniTask.ToCoroutine(async () =>
        {
            var driver = CreateDriver();
            SetupEmptyBoard();
            _matchState.ActivePlayerSlotValue = 0;

            await driver.StartAsync(MakeConfig(), 1, "easy", CancellationToken.None);

            // Fire event for human's turn (slot 0, bot is slot 1)
            _matchState.CurrentPlayerChangedSubject.OnNext(new CurrentPlayerChangedEvent(0));
            await UniTask.DelayFrame(3);

            _matchState.SubmittedCommands.Should().BeEmpty();
            driver.Dispose();
        });

        // ══════════════════════════════════════════════
        //  ADR-6: Single-flight + stale command guard
        // ══════════════════════════════════════════════

        [UnityTest]
        public IEnumerator WhenDisposedDuringTurn_ThenCancelsTurn() => UniTask.ToCoroutine(async () =>
        {
            var driver = CreateDriver();
            SetupEmptyBoard();
            _matchState.ActivePlayerSlotValue = 0;

            // Engine that never completes (simulates long computation)
            _engine.ChooseMoveAsync(Arg.Any<BotDecisionRequest>(), Arg.Any<BotProfileData>(),
                    Arg.Any<CancellationToken>())
                .Returns(info =>
                {
                    var ct = info.ArgAt<CancellationToken>(2);
                    return UniTask.Create(async () =>
                    {
                        await UniTask.Delay(10000, cancellationToken: ct);
                        return new CellId(0, 0);
                    });
                });

            await driver.StartAsync(MakeConfig(), 1, "easy", CancellationToken.None);

            // Trigger bot's turn
            _matchState.ActivePlayerSlotValue = 1;
            _matchState.CurrentPlayerChangedSubject.OnNext(new CurrentPlayerChangedEvent(1));
            await UniTask.DelayFrame(2); // let TriggerTurnAsync start

            // Dispose while engine is thinking
            driver.Dispose();
            await UniTask.DelayFrame(2);

            _matchState.SubmittedCommands.Should().BeEmpty("bot was cancelled before submitting");
        });

        // ══════════════════════════════════════════════
        //  ADR-12: Rejection handling
        // ══════════════════════════════════════════════

        [UnityTest]
        public IEnumerator WhenCommandRejectedOnce_ThenRetriesSuccessfully() => UniTask.ToCoroutine(async () =>
        {
            var driver = CreateDriver();
            SetupEmptyBoard();
            _matchState.ActivePlayerSlotValue = 0;

            var move = new CellId(1, 1);
            _engine.ChooseMoveAsync(Arg.Any<BotDecisionRequest>(), Arg.Any<BotProfileData>(),
                    Arg.Any<CancellationToken>())
                .Returns(UniTask.FromResult(move));

            // First submit will be rejected (no CommandSequence increment)
            _matchState.AcceptCommands = false;
            _matchState.OnSubmitCommand = _ =>
            {
                // After first rejection, start accepting
                _matchState.AcceptCommands = true;
            };

            await driver.StartAsync(MakeConfig(), 1, "easy", CancellationToken.None);

            _matchState.ActivePlayerSlotValue = 1;
            _matchState.CurrentPlayerChangedSubject.OnNext(new CurrentPlayerChangedEvent(1));
            await WaitForSubmitAsync(minCount: 2);

            // Should have submitted at least 2 times (original + retry)
            _matchState.SubmittedCommands.Count.Should().BeGreaterThanOrEqualTo(2);
            driver.Dispose();
        });

        [UnityTest]
        public IEnumerator WhenAllAttemptsRejected_ThenDisablesBot() => UniTask.ToCoroutine(async () =>
        {
            var driver = CreateDriver();
            SetupEmptyBoard();
            _matchState.ActivePlayerSlotValue = 0;

            var previousIgnore = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;

            var move = new CellId(1, 1);
            _engine.ChooseMoveAsync(Arg.Any<BotDecisionRequest>(), Arg.Any<BotProfileData>(),
                    Arg.Any<CancellationToken>())
                .Returns(UniTask.FromResult(move));

            // Reject all submits: command sequence never increments.
            _matchState.AcceptCommands = false;

            await driver.StartAsync(MakeConfig(), 1, "easy", CancellationToken.None);

            _matchState.ActivePlayerSlotValue = 1;
            _matchState.CurrentPlayerChangedSubject.OnNext(new CurrentPlayerChangedEvent(1));
            try
            {
                await WaitForSubmitAsync(minCount: 3);

                _matchState.SubmittedCommands.Count.Should().BeGreaterThanOrEqualTo(3);
                driver.IsDisabled.CurrentValue.Should().BeTrue();
            }
            finally
            {
                LogAssert.ignoreFailingMessages = previousIgnore;
                driver.Dispose();
            }
        });

        [Test]
        public void WhenDisposed_ThenIsBusyIsFalse()
        {
            var driver = CreateDriver();
            driver.IsBusy.CurrentValue.Should().BeFalse();
            driver.Dispose();
        }

        // ══════════════════════════════════════════════
        //  ADR-6: Stale command guards (gaps)
        // ══════════════════════════════════════════════

        [UnityTest]
        public IEnumerator WhenCommandSequenceChangesWhileBotThinking_ThenDoesNotSubmitMove() =>
            UniTask.ToCoroutine(async () =>
            {
                // Arrange
                var driver = CreateDriver();
                SetupEmptyBoard();
                _matchState.ActivePlayerSlotValue = 0;
                var (entered, release) = ConfigureBarrierEngine();

                await driver.StartAsync(MakeConfig(), 1, "easy", CancellationToken.None);

                // Trigger bot's turn
                _matchState.ActivePlayerSlotValue = 1;
                _matchState.CommandSequenceValue = 1;
                _matchState.CurrentPlayerChangedSubject.OnNext(new CurrentPlayerChangedEvent(1));
                await WaitUntilEngineEnteredAsync(entered);

                // Simulate external move accepted (CommandSequence changes)
                _matchState.CommandSequenceValue = 2;
                release.TrySetResult();
                await WaitUntilNotBusyAsync(driver);

                await _engine.Received(1).ChooseMoveAsync(Arg.Any<BotDecisionRequest>(),
                    Arg.Any<BotProfileData>(), Arg.Any<CancellationToken>());

                // Assert: stale move discarded
                _matchState.SubmittedCommands.Should().BeEmpty(
                    "driver must discard move when CommandSequence changed (ADR-6)");
                driver.IsBusy.CurrentValue.Should().BeFalse();

                driver.Dispose();
            });

        [UnityTest]
        public IEnumerator WhenActivePlayerSlotChangesWhileBotThinking_ThenDoesNotSubmitMove() =>
            UniTask.ToCoroutine(async () =>
            {
                // Arrange
                var driver = CreateDriver();
                SetupEmptyBoard();
                _matchState.ActivePlayerSlotValue = 0;
                var (entered, release) = ConfigureBarrierEngine();

                await driver.StartAsync(MakeConfig(), 1, "easy", CancellationToken.None);

                // Trigger bot's turn
                _matchState.ActivePlayerSlotValue = 1;
                _matchState.CommandSequenceValue = 1;
                _matchState.CurrentPlayerChangedSubject.OnNext(new CurrentPlayerChangedEvent(1));
                await WaitUntilEngineEnteredAsync(entered);

                // Active player changes to human (slot guard)
                _matchState.ActivePlayerSlotValue = 0;
                release.TrySetResult();
                await WaitUntilNotBusyAsync(driver);

                await _engine.Received(1).ChooseMoveAsync(Arg.Any<BotDecisionRequest>(),
                    Arg.Any<BotProfileData>(), Arg.Any<CancellationToken>());

                // Assert
                _matchState.SubmittedCommands.Should().BeEmpty(
                    "driver must discard move when ActivePlayerSlot no longer matches bot (ADR-6)");
                driver.IsBusy.CurrentValue.Should().BeFalse();

                driver.Dispose();
            });

        [UnityTest]
        public IEnumerator WhenRoundFinishedFiresWhileBotThinking_ThenDoesNotSubmit() =>
            UniTask.ToCoroutine(async () =>
            {
                // Arrange
                var driver = CreateDriver();
                SetupEmptyBoard();
                _matchState.ActivePlayerSlotValue = 0;
                var (entered, release) = ConfigureBarrierEngine();

                await driver.StartAsync(MakeConfig(), 1, "easy", CancellationToken.None);

                _matchState.ActivePlayerSlotValue = 1;
                _matchState.CommandSequenceValue = 1;
                _matchState.CurrentPlayerChangedSubject.OnNext(new CurrentPlayerChangedEvent(1));
                await WaitUntilEngineEnteredAsync(entered);

                // Fire RoundFinished during engine computation
                _matchState.RoundFinishedSubject.OnNext(
                    new RoundFinishedEvent(Runtime.Gameplay.ECS.GameStatus.Win, 0, null));
                release.TrySetResult();
                await WaitUntilNotBusyAsync(driver);

                await _engine.Received(1).ChooseMoveAsync(Arg.Any<BotDecisionRequest>(),
                    Arg.Any<BotProfileData>(), Arg.Any<CancellationToken>());

                // Assert
                _matchState.SubmittedCommands.Should().BeEmpty(
                    "driver must cancel and not submit when RoundFinished fires");
                driver.IsBusy.CurrentValue.Should().BeFalse();

                driver.Dispose();
            });

        [UnityTest]
        public IEnumerator WhenIsMatchActiveFalseWhileBotThinking_ThenDoesNotSubmit() =>
            UniTask.ToCoroutine(async () =>
            {
                // Arrange
                var driver = CreateDriver();
                SetupEmptyBoard();
                _matchState.ActivePlayerSlotValue = 0;
                var (entered, release) = ConfigureBarrierEngine();

                await driver.StartAsync(MakeConfig(), 1, "easy", CancellationToken.None);

                _matchState.ActivePlayerSlotValue = 1;
                _matchState.CommandSequenceValue = 1;
                _matchState.CurrentPlayerChangedSubject.OnNext(new CurrentPlayerChangedEvent(1));
                await WaitUntilEngineEnteredAsync(entered);

                // Match becomes inactive (no RoundFinished fired)
                _matchState.IsMatchActive = false;
                release.TrySetResult();
                await WaitUntilNotBusyAsync(driver);

                await _engine.Received(1).ChooseMoveAsync(Arg.Any<BotDecisionRequest>(),
                    Arg.Any<BotProfileData>(), Arg.Any<CancellationToken>());

                // Assert
                _matchState.SubmittedCommands.Should().BeEmpty(
                    "driver must not submit when IsMatchActive becomes false");
                driver.IsBusy.CurrentValue.Should().BeFalse();

                driver.Dispose();
            });

        [UnityTest]
        public IEnumerator WhenEngineReturnsAfterCancellation_ThenDriverDoesNotSubmit() =>
            UniTask.ToCoroutine(async () =>
            {
                // Arrange: engine that ignores CancellationToken and returns a move anyway
                var driver = CreateDriver();
                SetupEmptyBoard();
                _matchState.ActivePlayerSlotValue = 0;
                var (entered, release) = ConfigureBarrierEngine(honorCancellation: false);

                await driver.StartAsync(MakeConfig(), 1, "easy", CancellationToken.None);

                _matchState.ActivePlayerSlotValue = 1;
                _matchState.CommandSequenceValue = 1;
                _matchState.CurrentPlayerChangedSubject.OnNext(new CurrentPlayerChangedEvent(1));
                await WaitUntilEngineEnteredAsync(entered);

                // Cancel via RoundFinished before engine returns
                _matchState.RoundFinishedSubject.OnNext(
                    new RoundFinishedEvent(Runtime.Gameplay.ECS.GameStatus.Draw, null, null));
                release.TrySetResult();
                await WaitUntilNotBusyAsync(driver);

                await _engine.Received(1).ChooseMoveAsync(Arg.Any<BotDecisionRequest>(),
                    Arg.Any<BotProfileData>(), Arg.Any<CancellationToken>());

                // Assert: even though engine returned a move, driver must not submit
                _matchState.SubmittedCommands.Should().BeEmpty(
                    "driver must not submit even if engine returns after cancellation");

                driver.Dispose();
            });

        // ══════════════════════════════════════════════
        //  Lifecycle & Recovery (§3.6)
        // ══════════════════════════════════════════════

        [UnityTest]
        public IEnumerator WhenStartAsyncAndItsAlreadyBotTurn_ThenSubmitsMoveImmediately() =>
            UniTask.ToCoroutine(async () =>
            {
                // Arrange
                var driver = CreateDriver();
                SetupEmptyBoard();
                _matchState.ActivePlayerSlotValue = 1; // already bot's turn
                _matchState.CommandSequenceValue = 0;

                var expectedMove = new CellId(1, 1);
                _engine.ChooseMoveAsync(Arg.Any<BotDecisionRequest>(), Arg.Any<BotProfileData>(),
                        Arg.Any<CancellationToken>())
                    .Returns(UniTask.FromResult(expectedMove));

                // Act: start when it's already bot turn — should trigger move without event
                await driver.StartAsync(MakeConfig(), 1, "easy", CancellationToken.None);
                await WaitForSubmitAsync();

                // Assert
                _matchState.SubmittedCommands.Should().ContainSingle();
                var submitted = _matchState.SubmittedCommands[0];
                submitted.Should().BeAssignableTo<MakeMoveCommand>();
                ((MakeMoveCommand)submitted).CellId.Should().Be(expectedMove);

                driver.Dispose();
            });

        [UnityTest]
        public IEnumerator WhenAllRetriesExhausted_ThenIsDisabledTrueAndIsBusyFalse() =>
            UniTask.ToCoroutine(async () =>
            {
                // Arrange
                var driver = CreateDriver();
                SetupEmptyBoard();
                _matchState.ActivePlayerSlotValue = 0;

                var previousIgnore = LogAssert.ignoreFailingMessages;
                LogAssert.ignoreFailingMessages = true;

                _engine.ChooseMoveAsync(Arg.Any<BotDecisionRequest>(), Arg.Any<BotProfileData>(),
                        Arg.Any<CancellationToken>())
                    .Returns(UniTask.FromResult(new CellId(1, 1)));

                // Reject all submits
                _matchState.AcceptCommands = false;

                await driver.StartAsync(MakeConfig(), 1, "easy", CancellationToken.None);

                // Trigger bot turn
                _matchState.ActivePlayerSlotValue = 1;
                _matchState.CurrentPlayerChangedSubject.OnNext(new CurrentPlayerChangedEvent(1));

                try
                {
                    await WaitForSubmitAsync(minCount: 3);

                    // Assert: bot disabled with IsBusy=false
                    driver.IsDisabled.CurrentValue.Should().BeTrue("bot should be disabled after all retries exhausted");
                    driver.IsBusy.CurrentValue.Should().BeFalse("IsBusy must be reset after exhausted retries");

                    // Assert: subsequent events are ignored
                    _matchState.SubmittedCommands.Clear();
                    _matchState.CurrentPlayerChangedSubject.OnNext(new CurrentPlayerChangedEvent(1));
                    await UniTask.DelayFrame(5);
                    _matchState.SubmittedCommands.Should().BeEmpty(
                        "disabled bot should ignore subsequent CurrentPlayerChanged events");
                }
                finally
                {
                    LogAssert.ignoreFailingMessages = previousIgnore;
                    driver.Dispose();
                }
            });

    }
}
