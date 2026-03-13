#nullable enable

using Runtime.Gameplay.ECS.Components;
using Runtime.Games.Battleship.ECS.Core;
using Scellecs.Morpeh;

namespace Runtime.Games.Battleship.ECS.Events
{
    public sealed class BoardEventPublishSystem : ISystem
    {
        private Filter _matchFilter = null!;
        private Stash<BoardDirtyComponent> _boardDirtyStash = null!;
        private Stash<PlayersComponent> _playersStash = null!;
        private Stash<BattleshipMarksChangedOneShot> _marksChangedStash = null!;

        public World World { get; set; } = null!;

        public void OnAwake()
        {
            _matchFilter = World.Filter.With<MatchTag>().With<BoardDirtyComponent>().Build();
            _boardDirtyStash = World.GetStash<BoardDirtyComponent>();
            _playersStash = World.GetStash<PlayersComponent>();
            _marksChangedStash = World.GetStash<BattleshipMarksChangedOneShot>();
        }

        public void OnUpdate(float deltaTime)
        {
            if (_matchFilter.IsEmpty())
                return;

            var matchEntity = _matchFilter.First();
            _boardDirtyStash.Remove(matchEntity);

            if (!_playersStash.Has(matchEntity))
                return;

            ref var players = ref _playersStash.Get(matchEntity);
            
            if (players.PlayerSlots.Length == 0)
                return;

            var payload = new BattleshipMarksChangedOneShot
            {
                ViewerSlot = players.PlayerSlots[0],
                SecondaryViewerSlot = players.PlayerSlots.Length > 1 ? players.PlayerSlots[1] : players.PlayerSlots[0],
                HasSecondaryViewer = players.PlayerSlots.Length > 1,
            };

            _marksChangedStash.Set(matchEntity, payload);
        }

        public void Dispose() { }
    }
}