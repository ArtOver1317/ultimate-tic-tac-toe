using Runtime.Gameplay.ECS;
using Runtime.Games.TicTacToe.Ultimate.Rules;
using Scellecs.Morpeh;

namespace Runtime.Games.TicTacToe.ECS
{
    public sealed class UltimateRestartRoundSystem : ISystem
    {
        public World World { get; set; }

        private Filter _matchFilter;
        private Stash<UltimateEpochComponent> _epochStash;
        private Stash<UltimateMiniBoardsComponent> _miniBoardsStash;
        private Stash<UltimateAllowedMajorsComponent> _allowedStash;
        private Stash<UltimateBigBoardWinLineComponent> _bigBoardWinLineStash;

        public void OnAwake()
        {
            _matchFilter = World.Filter
                .With<MatchTag>()
                .With<RoundRestartedOneShot>()
                .With<UltimateEpochComponent>()
                .With<UltimateMiniBoardsComponent>()
                .With<UltimateAllowedMajorsComponent>()
                .With<UltimateBigBoardWinLineComponent>()
                .Build();

            _epochStash = World.GetStash<UltimateEpochComponent>();
            _miniBoardsStash = World.GetStash<UltimateMiniBoardsComponent>();
            _allowedStash = World.GetStash<UltimateAllowedMajorsComponent>();
            _bigBoardWinLineStash = World.GetStash<UltimateBigBoardWinLineComponent>();
        }

        public void OnUpdate(float deltaTime)
        {
            foreach (var entity in _matchFilter)
            {
                ref var epoch = ref _epochStash.Get(entity);
                ref var miniBoards = ref _miniBoardsStash.Get(entity);
                ref var allowed = ref _allowedStash.Get(entity);
                ref var bigBoardWinLine = ref _bigBoardWinLineStash.Get(entity);

                epoch.Value++;

                if (miniBoards.Statuses == null || miniBoards.Statuses.Length != 9)
                {
                    miniBoards.Statuses = new MiniBoardStatus[9];
                }

                for (var i = 0; i < miniBoards.Statuses.Length; i++)
                {
                    miniBoards.Statuses[i] = MiniBoardStatus.InProgress;
                }

                allowed.Value = AllowedMajors.All;
                bigBoardWinLine.HasValue = false;
                bigBoardWinLine.Value = default;
            }
        }

        public void Dispose() { }
    }
}