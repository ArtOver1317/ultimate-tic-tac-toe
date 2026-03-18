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
using Runtime.GameModes.Wizard.Modes;
using Runtime.Gameplay.Shared;
using Runtime.Games.TicTacToe.AI;
using Runtime.Games.TicTacToe.AI.Core;
using Runtime.Games.TicTacToe.AI.Profiles;
using Runtime.Games.TicTacToe.AI.Turns;
using Runtime.Games.TicTacToe.Moves;
using UnityEditor;
using UnityEngine.TestTools;
using EcsGameStatus = Runtime.Gameplay.Shared.EcsGameStatus;

namespace Tests.PlayMode.Games.TicTacToe.AI
{
    /// <summary>
    /// PlayMode tests for BotTurnDriver PreMoveDelay timing behaviour.
    /// PlayMode is required because PreMoveDelay uses UniTask.Delay which needs Unity PlayerLoop.
    /// </summary>
    [TestFixture]
    [Category("Unit")]
    public class BotTurnDriverPreMoveDelayTests
    {
        // ── Fake match state (same as EditMode BotTurnDriverTests) ──

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

            public List<IGameplayCommand> SubmittedCommands { get; } = new();
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
            }

            public void Dispose() { }
        }

        // ── Fields ──

        private FakeMatchState _matchState = null!;
        private IBotDecisionEngine _engine = null!;
        private IBotProfileCatalog _catalog = null!;
        private IClassicWinLengthProvider _winLengthProvider = null!;
        private BotProfile _profile = null!;

        [SetUp]
        public void SetUp()
        {
            _matchState = new FakeMatchState();
            _engine = Substitute.For<IBotDecisionEngine>();
            _catalog = Substitute.For<IBotProfileCatalog>();
            _winLengthProvider = Substitute.For<IClassicWinLengthProvider>();

            _profile = UnityEngine.ScriptableObject.CreateInstance<BotProfile>();

            // Set PreMoveDelay to 300ms
            using (var so = new SerializedObject(_profile))
            {
                so.FindProperty("PreMoveDelayMs").intValue = 300;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            _catalog.TryGet("easy", out Arg.Any<BotProfile?>())
                .Returns(x =>
                {
                    x[1] = _profile;
                    return true;
                });

            _winLengthProvider.GetWinLength(3).Returns(3);
        }

        [TearDown]
        public void TearDown()
        {
            if (_profile != null)
                UnityEngine.Object.DestroyImmediate(_profile);
        }

        private BotTurnDriver CreateDriver() =>
            new(_matchState, _engine, _catalog, _winLengthProvider);

        private static readonly TimeSpan SubmitTimeout = TimeSpan.FromSeconds(5);

        private async UniTask WaitForSubmitAsync(int minCount = 1)
        {
            var deadline = DateTime.UtcNow + SubmitTimeout;
            while (DateTime.UtcNow < deadline)
            {
                if (_matchState.SubmittedCommands.Count >= minCount)
                    return;

                await UniTask.Delay(50, DelayType.UnscaledDeltaTime);
            }

            Assert.Fail($"Submit command was not received within {SubmitTimeout.TotalSeconds:0.#}s.");
        }

        private (UniTaskCompletionSource entered, UniTaskCompletionSource release) ConfigureBarrierEngine()
        {
            var entered = new UniTaskCompletionSource();
            var release = new UniTaskCompletionSource();

            _engine.ChooseMoveAsync(Arg.Any<BotDecisionRequest>(), Arg.Any<BotProfileData>(),
                    Arg.Any<CancellationToken>())
                .Returns(info =>
                {
                    var ct = info.ArgAt<CancellationToken>(2);
                    return UniTask.Create(async () =>
                    {
                        entered.TrySetResult();
                        await release.Task.AttachExternalCancellation(ct);
                        return new CellId(1, 1);
                    });
                });

            return (entered, release);
        }

        private static async UniTask WaitUntilEngineEnteredAsync(UniTaskCompletionSource entered)
        {
            var winner = await UniTask.WhenAny(entered.Task, UniTask.Delay(2000));
            winner.Should().Be(0, "engine must start after PreMoveDelay in this test path");
        }

        private static GameLaunchConfig MakeConfig()
        {
            var gameConfig = new TicTacToeConfig(3, false);
            var opponentConfig = new BotOpponentConfig("easy");
            return new GameLaunchConfig("tic-tac-toe", gameConfig, opponentConfig);
        }

        private void SetupEmptyBoard()
        {
            var cells = new PlayerMark[9];
            _matchState.SetCells(3, cells);
            _matchState.ActivePlayerSlotValue = 1;
            _matchState.CommandSequenceValue = 0;
        }

        // ══════════════════════════════════════════════
        //  PreMoveDelay timing (§3.4)
        // ══════════════════════════════════════════════

        [UnityTest]
        public IEnumerator WhenPreMoveDelaySet_ThenDelaysBeforeSubmit() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var driver = CreateDriver();
            SetupEmptyBoard();
            _matchState.ActivePlayerSlotValue = 0;

            _engine.ChooseMoveAsync(Arg.Any<BotDecisionRequest>(), Arg.Any<BotProfileData>(),
                    Arg.Any<CancellationToken>())
                .Returns(UniTask.FromResult(new CellId(1, 1)));

            await driver.StartAsync(MakeConfig(), 1, "easy", CancellationToken.None);

            // Act: trigger bot's turn and measure time
            _matchState.ActivePlayerSlotValue = 1;
            _matchState.CommandSequenceValue = 1;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            _matchState.CurrentPlayerChangedSubject.OnNext(new CurrentPlayerChangedEvent(1));

            // Check that nothing submitted immediately (~100ms window)
            await UniTask.Delay(100);
            _matchState.SubmittedCommands.Should().BeEmpty(
                "bot should not submit immediately when PreMoveDelay is set");
            driver.IsBusy.CurrentValue.Should().BeTrue("bot should be busy during delay");

            // Wait for the submit to happen
            await WaitForSubmitAsync();
            sw.Stop();

            // Assert: submit happened with noticeable delay
            _matchState.SubmittedCommands.Should().ContainSingle();
            sw.ElapsedMilliseconds.Should().BeGreaterThan(150,
                "submit should be delayed substantially (PreMoveDelay=300ms)");
            driver.IsBusy.CurrentValue.Should().BeFalse();

            driver.Dispose();
        });

        [UnityTest]
        public IEnumerator WhenRoundFinishedFiresDuringPreMoveDelay_ThenDoesNotSubmit() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var driver = CreateDriver();
            SetupEmptyBoard();
            _matchState.ActivePlayerSlotValue = 0;

            var (entered, release) = ConfigureBarrierEngine();

            await driver.StartAsync(MakeConfig(), 1, "easy", CancellationToken.None);

            // Act: trigger turn and immediately finish round while PreMoveDelay is active
            _matchState.ActivePlayerSlotValue = 1;
            _matchState.CommandSequenceValue = 1;
            _matchState.CurrentPlayerChangedSubject.OnNext(new CurrentPlayerChangedEvent(1));
            _matchState.RoundFinishedSubject.OnNext(
                new RoundFinishedEvent(EcsGameStatus.Win, 0, null));

            await UniTask.Delay(400);

            // Assert: engine should not even start; submit must stay empty
            entered.Task.Status.Should().NotBe(UniTaskStatus.Succeeded,
                "turn must be cancelled during PreMoveDelay before engine starts");
            _matchState.SubmittedCommands.Should().BeEmpty(
                "cancel during PreMoveDelay must prevent submission");
            driver.IsBusy.CurrentValue.Should().BeFalse();

            release.TrySetResult();
            driver.Dispose();
        });

        [UnityTest]
        public IEnumerator WhenIsMatchActiveFalseDuringPreMoveDelay_ThenDoesNotSubmit() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var driver = CreateDriver();
            SetupEmptyBoard();
            _matchState.ActivePlayerSlotValue = 0;

            var (entered, release) = ConfigureBarrierEngine();

            await driver.StartAsync(MakeConfig(), 1, "easy", CancellationToken.None);

            // Act: trigger turn and invalidate match during PreMoveDelay
            _matchState.ActivePlayerSlotValue = 1;
            _matchState.CommandSequenceValue = 1;
            _matchState.CurrentPlayerChangedSubject.OnNext(new CurrentPlayerChangedEvent(1));

            await UniTask.Delay(100);
            driver.IsBusy.CurrentValue.Should().BeTrue("bot should be in PreMoveDelay window before deactivation");
            _matchState.IsMatchActive = false;

            await UniTask.Delay(350);

            // Assert: deactivation during PreMoveDelay prevents engine start and submit
            entered.Task.Status.Should().NotBe(UniTaskStatus.Succeeded,
                "inactive match during PreMoveDelay should stop turn before engine call");
            _ = _engine.DidNotReceive().ChooseMoveAsync(Arg.Any<BotDecisionRequest>(),
                Arg.Any<BotProfileData>(), Arg.Any<CancellationToken>());
            _matchState.SubmittedCommands.Should().BeEmpty(
                "inactive match during PreMoveDelay must prevent submission");
            driver.IsBusy.CurrentValue.Should().BeFalse();

            release.TrySetResult();
            driver.Dispose();
        });
    }
}
