#nullable enable

using System;
using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using Runtime.Gameplay;
using Runtime.Gameplay.Shared;
using Runtime.Games.TicTacToe.AI.Ultimate.Core;
using Runtime.Games.TicTacToe.AI.Ultimate.Execution;
using Runtime.Games.TicTacToe.Ultimate;
using Runtime.Games.TicTacToe.Ultimate.Rules;
using GameStatus = Runtime.Gameplay.GameStatus;

namespace Tests.EditMode.Games.TicTacToe.AI.Ultimate.Search
{
    [TestFixture]
    [Category("Unit")]
    public class UltimateBotStateReaderTests
    {
        [Test]
        public void WhenAllowedMajorSingleAndCellsEmpty_ThenBuildsLegalMovesInStableOrder()
        {
            var gameplay = new FakeGameplaySnapshotProvider(
                activePlayerSlot: 0,
                commandSequence: 42,
                occupiedCells: Array.Empty<CellId>());

            var ultimate = new FakeUltimateSnapshotProvider(
                matchStatus: GameStatus.InProgress,
                allowedMajors: new AllowedMajors(1 << 4),
                miniBoards: BuildMiniBoards(MiniBoardStatus.InProgress));

            var sut = new UltimateBotStateReader(gameplay, ultimate);
            var turnId = new BotTurnId(42, 0);
            var profile = CreateProfile();

            var built = sut.TryBuildDecisionRequest(
                botSlot: 0,
                turnId,
                profile,
                new FakeBotRngSession(),
                out var request,
                out var failReason);

            built.Should().BeTrue();
            failReason.Should().BeNull();
            request.LegalMovesStable.Should().HaveCount(9);
            request.LegalMovesStable[0].Should().Be(new CellId(4, 0));
            request.LegalMovesStable[8].Should().Be(new CellId(4, 8));
        }

        [Test]
        public void WhenMiniBoardNotInProgress_ThenExcludesMajorFromLegalMoves()
        {
            var gameplay = new FakeGameplaySnapshotProvider(
                activePlayerSlot: 0,
                commandSequence: 5,
                occupiedCells: Array.Empty<CellId>());

            var miniBoards = BuildMiniBoards(MiniBoardStatus.InProgress);
            miniBoards[0] = MiniBoardStatus.Draw;

            var ultimate = new FakeUltimateSnapshotProvider(
                matchStatus: GameStatus.InProgress,
                allowedMajors: AllowedMajors.All,
                miniBoards: miniBoards);

            var sut = new UltimateBotStateReader(gameplay, ultimate);

            var built = sut.TryBuildDecisionRequest(
                botSlot: 0,
                new BotTurnId(5, 0),
                CreateProfile(),
                new FakeBotRngSession(),
                out var request,
                out var failReason);

            built.Should().BeTrue();
            failReason.Should().BeNull();
            request.LegalMovesStable.Should().NotContain(move => move.Major == 0);
            request.LegalMovesStable.Should().HaveCount(72);
        }

        [Test]
        public void WhenNoLegalMovesAndMatchInProgress_ThenReturnsNoLegalMovesInconsistentState()
        {
            var occupied = new List<CellId>(9);
            
            for (var minor = 0; minor < 9; minor++)
            {
                occupied.Add(new CellId(3, minor));
            }

            var gameplay = new FakeGameplaySnapshotProvider(
                activePlayerSlot: 1,
                commandSequence: 99,
                occupiedCells: occupied);

            var ultimate = new FakeUltimateSnapshotProvider(
                matchStatus: GameStatus.InProgress,
                allowedMajors: new AllowedMajors(1 << 3),
                miniBoards: BuildMiniBoards(MiniBoardStatus.InProgress));

            var sut = new UltimateBotStateReader(gameplay, ultimate);

            var built = sut.TryBuildDecisionRequest(
                botSlot: 1,
                new BotTurnId(99, 1),
                CreateProfile(),
                new FakeBotRngSession(),
                out _,
                out var failReason);

            built.Should().BeFalse();
            failReason.Should().Be(BotFailureReason.NoLegalMovesInconsistentState);
        }

        [Test]
        public void WhenTryBuildDecisionRequestAndNotBotTurn_ThenReturnsFalseWithoutFailReason()
        {
            var gameplay = new FakeGameplaySnapshotProvider(
                activePlayerSlot: 1,
                commandSequence: 7,
                occupiedCells: Array.Empty<CellId>());

            var ultimate = new FakeUltimateSnapshotProvider(
                matchStatus: GameStatus.InProgress,
                allowedMajors: AllowedMajors.All,
                miniBoards: BuildMiniBoards(MiniBoardStatus.InProgress));

            var sut = new UltimateBotStateReader(gameplay, ultimate);

            var built = sut.TryBuildDecisionRequest(
                botSlot: 0,
                new BotTurnId(7, 1),
                CreateProfile(),
                new FakeBotRngSession(),
                out _,
                out var failReason);

            built.Should().BeFalse();
            failReason.Should().BeNull();
        }

        [Test]
        public void WhenTryBuildDecisionRequestAndTurnIdStale_ThenReturnsFalseWithoutFailReason()
        {
            var gameplay = new FakeGameplaySnapshotProvider(
                activePlayerSlot: 0,
                commandSequence: 15,
                occupiedCells: Array.Empty<CellId>());

            var ultimate = new FakeUltimateSnapshotProvider(
                matchStatus: GameStatus.InProgress,
                allowedMajors: AllowedMajors.All,
                miniBoards: BuildMiniBoards(MiniBoardStatus.InProgress));

            var sut = new UltimateBotStateReader(gameplay, ultimate);

            var built = sut.TryBuildDecisionRequest(
                botSlot: 0,
                new BotTurnId(14, 0),
                CreateProfile(),
                new FakeBotRngSession(),
                out _,
                out var failReason);

            built.Should().BeFalse();
            failReason.Should().BeNull();
        }

        [Test]
        public void WhenTryBuildDecisionRequestAndMatchNotInProgress_ThenReturnsFalseWithoutFailReason()
        {
            var gameplay = new FakeGameplaySnapshotProvider(
                activePlayerSlot: 0,
                commandSequence: 5,
                occupiedCells: Array.Empty<CellId>());

            var ultimate = new FakeUltimateSnapshotProvider(
                matchStatus: GameStatus.Draw,
                allowedMajors: AllowedMajors.All,
                miniBoards: BuildMiniBoards(MiniBoardStatus.InProgress));

            var sut = new UltimateBotStateReader(gameplay, ultimate);

            var built = sut.TryBuildDecisionRequest(
                botSlot: 0,
                new BotTurnId(5, 0),
                CreateProfile(),
                new FakeBotRngSession(),
                out _,
                out var failReason);

            built.Should().BeFalse();
            failReason.Should().BeNull();
        }

        private static UltimateBotDifficultyProfileData CreateProfile() =>
            new(
                profileId: "test",
                profileVersion: "1.0.0",
                profileHash: new string('a', 64),
                timeBudgetMs: 100,
                minSearchDepth: 1,
                maxSearchDepth: 2,
                maxEvaluatedNodes: 500,
                topCandidateCount: 3,
                noise: 0.1f,
                mustWinGlobalNowProbability: 1f,
                mustBlockGlobalNowProbability: 1f,
                mustWinLocalNowProbability: 1f,
                mustBlockLocalNowProbability: 1f,
                useSeed: false,
                seed: 0,
                preMoveDelayMs: 0,
                enableDiagnostics: false,
                weights: EvaluationWeights.Default);

        private static MiniBoardStatus[] BuildMiniBoards(MiniBoardStatus value)
        {
            var result = new MiniBoardStatus[9];
          
            for (var i = 0; i < result.Length; i++)
            {
                result[i] = value;
            }

            return result;
        }

        private sealed class FakeGameplaySnapshotProvider : IGameplaySnapshotProvider
        {
            private readonly HashSet<CellId> _occupied;

            public FakeGameplaySnapshotProvider(int activePlayerSlot, long commandSequence, IReadOnlyList<CellId> occupiedCells)
            {
                ActivePlayerSlot = activePlayerSlot;
                CommandSequence = commandSequence;
                _occupied = new HashSet<CellId>(occupiedCells);
            }

            public int ActivePlayerSlot { get; }
            public long CommandSequence { get; }
            public CellId? LastMove => null;

            public int GetCellSlot(CellId cellId) => _occupied.Contains(cellId) ? 0 : -1;

            public IReadOnlyList<CellSnapshot> GetAllCells()
                => Array.Empty<CellSnapshot>();
        }

        private sealed class FakeUltimateSnapshotProvider : IUltimateGameplaySnapshotProvider
        {
            private readonly MiniBoardStatus[] _miniBoards;

            public FakeUltimateSnapshotProvider(GameStatus matchStatus, AllowedMajors allowedMajors, MiniBoardStatus[] miniBoards)
            {
                _miniBoards = miniBoards;
                CurrentAllowedMajors = allowedMajors;
                CurrentMatch = new UltimateMatchResult(matchStatus, PlayerMark.None, null);
            }

            public ulong Epoch => 0;
            public AllowedMajors CurrentAllowedMajors { get; }
            public UltimateMatchResult CurrentMatch { get; }

            public void CopyMiniBoardsTo(Span<MiniBoardStatus> destination) => _miniBoards.CopyTo(destination);
        }

        private sealed class FakeBotRngSession : IBotRngSession
        {
            public uint NextUInt() => 1;
            public float NextFloat01() => 0.1f;
            public int NextInt(int minInclusive, int maxExclusive) => minInclusive;
        }
    }
}
