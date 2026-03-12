using System;
using Runtime.Gameplay.ECS;
using Runtime.Gameplay.ECS.Components;
using Runtime.Gameplay.Shared;
using Runtime.Games.TicTacToe.Ultimate.Rules;
using Scellecs.Morpeh;

namespace Runtime.Games.TicTacToe.ECS
{
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

        public UltimateRulesStateUpdateSystem(IUltimateRulesEngine rulesEngine)
        {
            _rulesEngine = rulesEngine ?? throw new ArgumentNullException(nameof(rulesEngine));
        }

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
                ref var applied = ref _appliedStash.Get(entity);
                ref var board = ref _boardStash.Get(entity);
                ref var fieldConfig = ref _fieldConfigStash.Get(entity);
                ref var miniBoards = ref _miniBoardsStash.Get(entity);
                ref var allowed = ref _allowedStash.Get(entity);
                ref var bigBoardWinLine = ref _bigBoardWinLineStash.Get(entity);
                ref var epoch = ref _epochStash.Get(entity);
                ref var status = ref _statusStash.Get(entity);

                if (miniBoards.Statuses == null || miniBoards.Statuses.Length != 9)
                {
                    throw new InvalidOperationException("UltimateMiniBoardsComponent must be initialized with 9 statuses.");
                }

                var result = _rulesEngine.EvaluateAfterMove(
                    board.Cells,
                    fieldConfig.OuterSize,
                    fieldConfig.InnerSize,
                    applied.CellId,
                    miniBoards.Statuses);

                if (!result.Match.IsValid())
                {
                    throw new InvalidOperationException("Ultimate rules engine returned invalid match result.");
                }

                if (result.MiniBoardDelta.HasValue)
                {
                    var delta = result.MiniBoardDelta.Value;
                    miniBoards.Statuses[delta.Major] = delta.NewStatus;
                    _miniBoardChangedStash.Set(entity, new UltimateMiniBoardStatusChangedOneShot
                    {
                        Epoch = epoch.Value,
                        Major = delta.Major,
                        NewStatus = delta.NewStatus,
                    });
                }

                var previousAllowed = allowed.Value;
                allowed.Value = result.AllowedMajors;

                if (previousAllowed != result.AllowedMajors)
                {
                    _allowedChangedStash.Set(entity, new UltimateAllowedMajorsChangedOneShot
                    {
                        Epoch = epoch.Value,
                        AllowedMajors = result.AllowedMajors,
                    });
                }

                switch (result.Match.Status)
                {
                    case Rules.GameStatus.InProgress:
                        status.Status = GameStatus.InProgress;
                        status.WinnerSlot = null;
                        status.WinLine = null;
                        bigBoardWinLine.HasValue = false;
                        bigBoardWinLine.Value = default;
                        break;

                    case Rules.GameStatus.Draw:
                        status.Status = GameStatus.Draw;
                        status.WinnerSlot = null;
                        status.WinLine = null;
                        bigBoardWinLine.HasValue = false;
                        bigBoardWinLine.Value = default;
                        _roundFinishedStash.Set(entity, new RoundFinishedOneShot
                        {
                            Status = status.Status,
                            WinnerSlot = status.WinnerSlot,
                            WinLine = status.WinLine,
                        });
                        break;

                    case Rules.GameStatus.Win:
                        status.Status = GameStatus.Win;
                        status.WinnerSlot = PlayerSlotMapping.MarkToSlot(result.Match.Winner);
                        status.WinLine = null;

                        if (!result.Match.BigBoardWinLine.HasValue)
                        {
                            throw new InvalidOperationException("Win result must include big-board win line.");
                        }

                        bigBoardWinLine.HasValue = true;
                        bigBoardWinLine.Value = result.Match.BigBoardWinLine.Value;

                        _roundFinishedStash.Set(entity, new RoundFinishedOneShot
                        {
                            Status = status.Status,
                            WinnerSlot = status.WinnerSlot,
                            WinLine = status.WinLine,
                        });
                        break;

                    case Rules.GameStatus.Timeout:
                        status.Status = GameStatus.Timeout;
                        status.WinnerSlot = result.Match.Winner == Moves.PlayerMark.None
                            ? null
                            : PlayerSlotMapping.MarkToSlot(result.Match.Winner);
                        status.WinLine = null;
                        bigBoardWinLine.HasValue = false;
                        bigBoardWinLine.Value = default;
                        _roundFinishedStash.Set(entity, new RoundFinishedOneShot
                        {
                            Status = status.Status,
                            WinnerSlot = status.WinnerSlot,
                            WinLine = status.WinLine,
                        });
                        break;

                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        public void Dispose() { }
    }
}