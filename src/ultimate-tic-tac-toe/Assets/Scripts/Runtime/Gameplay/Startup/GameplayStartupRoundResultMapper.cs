#nullable enable
using System;
using Runtime.Gameplay.Shared;
using Runtime.Games.TicTacToe.Ultimate.Rules;

namespace Runtime.Gameplay.Startup
{
    internal sealed class GameplayStartupRoundResultMapper
    {
        private readonly GameplayStartupDependencies _dependencies;
        private readonly GameplayStartupRuntimeState _state;

        private GameplayStartupBotServices Bot => _dependencies.Bot;
        private GameplayStartupUiState UiState => _state.Ui;

        public GameplayStartupRoundResultMapper(GameplayStartupDependencies dependencies, GameplayStartupRuntimeState state)
        {
            _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
            _state = state ?? throw new ArgumentNullException(nameof(state));
        }

        internal GameResult BuildGameResult(RoundFinishedEvent evt) => 
            TryBuildUltimateGameResult(out var ultimateResult) ? ultimateResult : BuildStandardGameResult(evt);

        internal bool TryGetUltimateBigBoardWinLine(out UltimateBigBoardWinLine line)
        {
            line = default;

            if (!TryGetUltimateMatch(out var match) || !match.BigBoardWinLine.HasValue)
                return false;

            line = match.BigBoardWinLine.Value;
            return true;
        }

        internal WinLine MapEcsWinLine(EcsWinLine ecsLine)
        {
            var rowDiff = ecsLine.End.Major - ecsLine.Start.Major;
            var colDiff = ecsLine.End.Minor - ecsLine.Start.Minor;

            WinLineDirection direction;

            if (rowDiff == 0)
                direction = WinLineDirection.Horizontal;
            else if (colDiff == 0)
                direction = WinLineDirection.Vertical;
            else if (colDiff > 0)
                direction = WinLineDirection.DiagonalMain;
            else
                direction = WinLineDirection.DiagonalAnti;

            var length = Math.Max(Math.Abs(rowDiff), Math.Abs(colDiff)) + 1;
            return new WinLine(ecsLine.Start, ecsLine.End, direction, length);
        }

        private bool TryBuildUltimateGameResult(out GameResult result)
        {
            result = default;

            if (!TryGetUltimateMatch(out var match))
                return false;

            result = match.Status switch
            {
                GameStatus.Win => BuildUltimateWinResult(match),
                GameStatus.Timeout => GameResult.Timeout(match.Winner),
                GameStatus.Draw => GameResult.Draw(),
                _ => GameResult.InProgress(),
            };

            return true;
        }

        private bool TryGetUltimateMatch(out UltimateMatchResult match)
        {
            match = default;

            if (UiState.FieldSpec is not { Kind: FieldKind.Ultimate } || Bot.UltimateSnapshotProvider == null)
                return false;

            match = Bot.UltimateSnapshotProvider.CurrentMatch;
            return true;
        }

        private GameResult BuildStandardGameResult(RoundFinishedEvent evt)
        {
            var winner = evt.WinnerSlot.HasValue
                ? PlayerSlotMapping.SlotToMark(evt.WinnerSlot.Value)
                : PlayerMark.None;

            var status = MapEcsStatus(evt.Status);

            return status switch
            {
                GameStatus.Win => winner == PlayerMark.None
                    ? GameResult.Draw()
                    : GameResult.Win(winner, evt.WinLine.HasValue ? MapEcsWinLine(evt.WinLine.Value) : CreateFallbackWinLine()),
                GameStatus.Timeout => GameResult.Timeout(winner),
                GameStatus.Draw => GameResult.Draw(),
                _ => GameResult.InProgress(),
            };
        }

        private static GameResult BuildUltimateWinResult(UltimateMatchResult match) => 
            !match.BigBoardWinLine.HasValue 
                ? throw new InvalidOperationException("Ultimate win result must include BigBoardWinLine.") 
                : GameResult.Win(match.Winner, MapUltimateBigBoardWinLine(match.BigBoardWinLine.Value));

        private static GameStatus MapEcsStatus(EcsGameStatus ecsStatus) => ecsStatus switch
        {
            EcsGameStatus.Win => GameStatus.Win,
            EcsGameStatus.Draw => GameStatus.Draw,
            EcsGameStatus.InProgress => GameStatus.InProgress,
            EcsGameStatus.Timeout => GameStatus.Timeout,
            _ => throw new ArgumentOutOfRangeException(nameof(ecsStatus), ecsStatus, null),
        };

        private static WinLine MapUltimateBigBoardWinLine(UltimateBigBoardWinLine line)
        {
            static CellId ToCellId(int major) => new(major / 3, major % 3);

            var start = ToCellId(line.Major0);
            var end = ToCellId(line.Major2);

            var rowDiff = end.Major - start.Major;
            var colDiff = end.Minor - start.Minor;

            var direction = rowDiff == 0
                ? WinLineDirection.Horizontal
                : colDiff == 0
                    ? WinLineDirection.Vertical
                    : colDiff > 0
                        ? WinLineDirection.DiagonalMain
                        : WinLineDirection.DiagonalAnti;

            return new WinLine(start, end, direction, 3);
        }

        private static WinLine CreateFallbackWinLine() =>
            new(new CellId(0, 0), new CellId(0, 0), WinLineDirection.Horizontal, 1);
    }
}