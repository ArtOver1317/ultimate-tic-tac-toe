using Runtime.Gameplay.ECS.Components;
using Runtime.Gameplay.Shared;
using Runtime.Games.TicTacToe.Ultimate;
using Runtime.Games.TicTacToe.Ultimate.Rules;
using Scellecs.Morpeh;

namespace Runtime.Games.TicTacToe.ECS
{
    /// <summary>
    /// Rejects move requests that target disallowed or already resolved mini-boards in ultimate mode.
    /// </summary>
    public sealed class UltimateAllowedMoveValidationSystem : ISystem
    {
        public World World { get; set; }

        private Filter _matchFilter;
        private Stash<MakeMoveRequest> _moveRequestStash;
        private Stash<UltimateAllowedMajorsComponent> _allowedStash;
        private Stash<UltimateMiniBoardsComponent> _miniBoardsStash;
        private Stash<MoveRejectedOneShot> _rejectedStash;

        public void OnAwake()
        {
            _matchFilter = World.Filter
                .With<MatchTag>()
                .With<MakeMoveRequest>()
                .With<UltimateAllowedMajorsComponent>()
                .With<UltimateMiniBoardsComponent>()
                .Build();

            _moveRequestStash = World.GetStash<MakeMoveRequest>();
            _allowedStash = World.GetStash<UltimateAllowedMajorsComponent>();
            _miniBoardsStash = World.GetStash<UltimateMiniBoardsComponent>();
            _rejectedStash = World.GetStash<MoveRejectedOneShot>();
        }

        public void OnUpdate(float deltaTime)
        {
            foreach (var entity in _matchFilter)
            {
                ref var request = ref _moveRequestStash.Get(entity);
                ref var allowed = ref _allowedStash.Get(entity);
                ref var miniBoards = ref _miniBoardsStash.Get(entity);

                if (miniBoards.Statuses == null || miniBoards.Statuses.Length != UltimateBoardConstants.MajorCount)
                {
                    Reject(entity, GameplayRejectionReason.Unknown);
                    _moveRequestStash.Remove(entity);
                    continue;
                }

                if (!allowed.Value.ContainsMajor(request.CellId.Major))
                {
                    Reject(entity, GameplayRejectionReason.ForbiddenMove);
                    _moveRequestStash.Remove(entity);
                    continue;
                }

                if (miniBoards.Statuses[request.CellId.Major] != MiniBoardStatus.InProgress)
                {
                    Reject(entity, GameplayRejectionReason.ForbiddenMove);
                    _moveRequestStash.Remove(entity);
                }
            }
        }

        private void Reject(Entity entity, GameplayRejectionReason reason) =>
            _rejectedStash.Set(entity, new MoveRejectedOneShot
            {
                CommandType = GameplayCommandType.MakeMove,
                Rejection = new CommandRejection(reason),
            });

        public void Dispose() { }
    }
}