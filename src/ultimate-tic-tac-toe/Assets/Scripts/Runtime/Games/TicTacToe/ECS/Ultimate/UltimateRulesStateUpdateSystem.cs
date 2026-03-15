using System;
using Runtime.Gameplay.ECS.Components;
using Runtime.Gameplay.Shared;
using Runtime.Games.TicTacToe.Ultimate.Rules;
using Scellecs.Morpeh;
using EcsGameStatus = Runtime.Gameplay.Shared.GameStatus;
using RulesGameStatus = Runtime.Games.TicTacToe.Rules.GameStatus;

namespace Runtime.Games.TicTacToe.ECS
{
    /// <summary>
    /// Updates ultimate-specific board state after an applied move and publishes one-shot ECS deltas.
    /// </summary>
    public sealed class UltimateRulesStateUpdateSystem : ISystem
    {
        public World World { get; set; }

        private readonly IUltimateRulesEngine _rulesEngine;

        private Filter _matchFilter;
        private Stash<MoveAppliedOneShot> _appliedStash;
        private Stash<BoardStateComponent> _boardStash;
        private Stash<FieldConfigComponent> _fieldConfigStash;
        private Stash<UltimateMiniBoardsComponent> _miniBoardsStash;
        private Stash<UltimateAllowedMajorsComponent> _allowedStash;
        private Stash<UltimateBigBoardWinLineComponent> _bigBoardWinLineStash;
        private Stash<UltimateEpochComponent> _epochStash;
        private Stash<UltimateAllowedMajorsChangedOneShot> _allowedChangedStash;
        private Stash<UltimateMiniBoardStatusChangedOneShot> _miniBoardChangedStash;
        private Stash<MatchStatusComponent> _statusStash;
        private Stash<RoundFinishedOneShot> _roundFinishedStash;

        public UltimateRulesStateUpdateSystem(IUltimateRulesEngine rulesEngine) =>
            _rulesEngine = rulesEngine ?? throw new ArgumentNullException(nameof(rulesEngine));

        public void OnAwake()
        {
            _matchFilter = World.Filter.With<MatchTag>().With<MoveAppliedOneShot>().Build();
            _appliedStash = World.GetStash<MoveAppliedOneShot>();
            _boardStash = World.GetStash<BoardStateComponent>();
            _fieldConfigStash = World.GetStash<FieldConfigComponent>();
            _miniBoardsStash = World.GetStash<UltimateMiniBoardsComponent>();
            _allowedStash = World.GetStash<UltimateAllowedMajorsComponent>();
            _bigBoardWinLineStash = World.GetStash<UltimateBigBoardWinLineComponent>();
            _epochStash = World.GetStash<UltimateEpochComponent>();
            _allowedChangedStash = World.GetStash<UltimateAllowedMajorsChangedOneShot>();
            _miniBoardChangedStash = World.GetStash<UltimateMiniBoardStatusChangedOneShot>();
            _statusStash = World.GetStash<MatchStatusComponent>();
            _roundFinishedStash = World.GetStash<RoundFinishedOneShot>();
        }

        public void OnUpdate(float deltaTime)
        {
            foreach (var entity in _matchFilter)
            {
                ProcessMatch(entity);
            }
        }

        private void ProcessMatch(Entity entity)
        {
            ref var applied = ref _appliedStash.Get(entity);
            ref var board = ref _boardStash.Get(entity);
            ref var fieldConfig = ref _fieldConfigStash.Get(entity);
            ref var miniBoards = ref _miniBoardsStash.Get(entity);
            ref var allowed = ref _allowedStash.Get(entity);
            ref var bigBoardWinLine = ref _bigBoardWinLineStash.Get(entity);
            ref var epoch = ref _epochStash.Get(entity);
            ref var status = ref _statusStash.Get(entity);

            ValidateMiniBoards(miniBoards.Statuses);

            var result = EvaluateRules(board, fieldConfig, applied, miniBoards.Statuses);

            ApplyMiniBoardDelta(entity, epoch.Value, ref miniBoards, in result);
            UpdateAllowedMajors(entity, epoch.Value, ref allowed, in result);

            if (UpdateMatchStatus(result.Match, ref status, ref bigBoardWinLine))
                PublishRoundFinished(entity, in status);
        }

        private UltimateRulesResult EvaluateRules(
            BoardStateComponent board,
            FieldConfigComponent fieldConfig,
            MoveAppliedOneShot applied,
            MiniBoardStatus[] miniBoardStatuses)
        {
            var result = _rulesEngine.EvaluateAfterMove(
                board.Cells,
                fieldConfig.OuterSize,
                fieldConfig.InnerSize,
                applied.CellId,
                miniBoardStatuses);

            return !result.Match.IsValid() 
                ? throw new InvalidOperationException("Ultimate rules engine returned invalid match result.") 
                : result;
        }

        private static void ValidateMiniBoards(MiniBoardStatus[] miniBoardStatuses)
        {
            if (miniBoardStatuses is not { Length: UltimateConstants.MiniBoardCount })
            {
                throw new InvalidOperationException(
                    $"UltimateMiniBoardsComponent must be initialized with {UltimateConstants.MiniBoardCount} statuses.");
            }
        }

        private void ApplyMiniBoardDelta(
            Entity entity,
            ulong epoch,
            ref UltimateMiniBoardsComponent miniBoards,
            in UltimateRulesResult result)
        {
            if (!result.MiniBoardDelta.HasValue)
                return;

            var delta = result.MiniBoardDelta.Value;
            miniBoards.Statuses[delta.Major] = delta.NewStatus;

            _miniBoardChangedStash.Set(entity, new UltimateMiniBoardStatusChangedOneShot
            {
                Epoch = epoch,
                Major = delta.Major,
                NewStatus = delta.NewStatus,
            });
        }

        private void UpdateAllowedMajors(
            Entity entity,
            ulong epoch,
            ref UltimateAllowedMajorsComponent allowed,
            in UltimateRulesResult result)
        {
            var previousAllowed = allowed.Value;
            allowed.Value = result.AllowedMajors;

            if (previousAllowed == result.AllowedMajors)
                return;

            _allowedChangedStash.Set(entity, new UltimateAllowedMajorsChangedOneShot
            {
                Epoch = epoch,
                AllowedMajors = result.AllowedMajors,
            });
        }

        private bool UpdateMatchStatus(
            UltimateMatchResult match,
            ref MatchStatusComponent status,
            ref UltimateBigBoardWinLineComponent bigBoardWinLine)
        {
            status.WinLine = null;

            switch (match.Status)
            {
                case RulesGameStatus.InProgress:
                    status.Status = EcsGameStatus.InProgress;
                    status.WinnerSlot = null;
                    ClearBigBoardWinLine(ref bigBoardWinLine);
                    return false;

                case RulesGameStatus.Draw:
                    status.Status = EcsGameStatus.Draw;
                    status.WinnerSlot = null;
                    ClearBigBoardWinLine(ref bigBoardWinLine);
                    return true;

                case RulesGameStatus.Win:
                    status.Status = EcsGameStatus.Win;
                    status.WinnerSlot = PlayerSlotMapping.MarkToSlot(match.Winner);
                    SetBigBoardWinLine(match, ref bigBoardWinLine);
                    return true;

                case RulesGameStatus.Timeout:
                    status.Status = EcsGameStatus.Timeout;
                    status.WinnerSlot = ToOptionalWinnerSlot(match.Winner);
                    ClearBigBoardWinLine(ref bigBoardWinLine);
                    return true;

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private void PublishRoundFinished(Entity entity, in MatchStatusComponent status) =>
            _roundFinishedStash.Set(entity, new RoundFinishedOneShot
            {
                Status = status.Status,
                WinnerSlot = status.WinnerSlot,
                WinLine = status.WinLine,
            });

        private static void ClearBigBoardWinLine(ref UltimateBigBoardWinLineComponent bigBoardWinLine)
        {
            bigBoardWinLine.HasValue = false;
            bigBoardWinLine.Value = default;
        }

        private static void SetBigBoardWinLine(
            UltimateMatchResult match,
            ref UltimateBigBoardWinLineComponent bigBoardWinLine)
        {
            if (!match.BigBoardWinLine.HasValue)
                throw new InvalidOperationException("Win result must include big-board win line.");

            bigBoardWinLine.HasValue = true;
            bigBoardWinLine.Value = match.BigBoardWinLine.Value;
        }

        private static int? ToOptionalWinnerSlot(Moves.PlayerMark winner) =>
            winner == Moves.PlayerMark.None
                ? null
                : PlayerSlotMapping.MarkToSlot(winner);

        public void Dispose() { }
    }
}