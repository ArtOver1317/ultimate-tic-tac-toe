using System;
using Runtime.Gameplay.ECS;
using Runtime.Games.TicTacToe.Rules;
using Scellecs.Morpeh;
using EcsGameStatus = Runtime.Gameplay.ECS.GameStatus;
using RulesGameStatus = Runtime.Games.TicTacToe.Rules.GameStatus;

namespace Runtime.Games.TicTacToe.ECS
{
    /// <summary>
    /// After a move is applied, evaluates the game rules to detect win/draw.
    /// Uses the existing <see cref="IRulesEngine"/> (ClassicRulesEngine) — stateless, deterministic (ADR-6).
    /// </summary>
    public sealed class RulesEvaluationSystem : ISystem
    {
        public World World { get; set; }

        private readonly IRulesEngine _rulesEngine;

        private Filter _matchFilter;
        private Stash<MoveAppliedOneShot> _appliedStash;
        private Stash<BoardStateComponent> _boardStash;
        private Stash<FieldConfigComponent> _fieldConfigStash;
        private Stash<MatchStatusComponent> _statusStash;
        private Stash<RoundFinishedOneShot> _roundFinishedStash;

        public RulesEvaluationSystem(IRulesEngine rulesEngine) =>
            _rulesEngine = rulesEngine ?? throw new ArgumentNullException(nameof(rulesEngine));

        public void OnAwake()
        {
            _matchFilter = World.Filter.With<MatchTag>().With<MoveAppliedOneShot>().Build();
            _appliedStash = World.GetStash<MoveAppliedOneShot>();
            _boardStash = World.GetStash<BoardStateComponent>();
            _fieldConfigStash = World.GetStash<FieldConfigComponent>();
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

                // Classic: boardSize = OuterSize. Ultimate: not yet supported in rules evaluation.
                var boardSize = fieldConfig.OuterSize;

                var result = _rulesEngine.Evaluate(board.Cells, boardSize, applied.CellId);

                if (result.Status == RulesGameStatus.InProgress)
                    continue;

                // Update match status
                ref var status = ref _statusStash.Get(entity);

                if (result.Status == RulesGameStatus.Win)
                {
                    status.Status = EcsGameStatus.Win;
                    status.WinnerSlot = TicTacToeEcsRegistrar.MarkToSlot(result.Winner);

                    if (result.WinLine.HasValue)
                    {
                        var wl = result.WinLine.Value;
                        status.WinLine = new EcsWinLine(wl.Start, wl.End);
                    }
                }
                else if (result.Status == RulesGameStatus.Draw)
                {
                    status.Status = EcsGameStatus.Draw;
                    status.WinnerSlot = null;
                    status.WinLine = null;
                }

                // Place RoundFinished one-shot for EventPublishSystem
                _roundFinishedStash.Set(entity, new RoundFinishedOneShot
                {
                    Status = status.Status,
                    WinnerSlot = status.WinnerSlot,
                    WinLine = status.WinLine,
                });
            }
        }

        public void Dispose() { }
    }
}
