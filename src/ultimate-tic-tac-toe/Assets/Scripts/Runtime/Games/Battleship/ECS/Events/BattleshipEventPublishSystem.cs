using Runtime.Gameplay.ECS.Components;
using Runtime.Games.Battleship.Core;
using Runtime.Games.Battleship.ECS.Core;
using Scellecs.Morpeh;

namespace Runtime.Games.Battleship.ECS.Events
{
    public sealed class BattleshipEventPublishSystem : ISystem
    {
        private readonly BattleshipGameplayEventStream _eventStream;

        private Filter _matchFilter;
        private Stash<BattleshipPhaseChangedOneShot> _phaseChangedStash;
        private Stash<BattleshipMarksChangedOneShot> _marksChangedStash;

        public BattleshipEventPublishSystem(BattleshipGameplayEventStream eventStream) =>
            _eventStream = eventStream ?? throw new System.ArgumentNullException(nameof(eventStream));

        public World World { get; set; }

        public void OnAwake()
        {
            _matchFilter = World.Filter.With<MatchTag>().Build();
            _phaseChangedStash = World.GetStash<BattleshipPhaseChangedOneShot>();
            _marksChangedStash = World.GetStash<BattleshipMarksChangedOneShot>();
        }

        public void OnUpdate(float deltaTime)
        {
            if (_matchFilter.IsEmpty())
                return;

            var matchEntity = _matchFilter.First();
            TryPublishPhaseChanged(matchEntity);
            TryPublishMarksChanged(matchEntity);
        }

        public void Dispose() { }

        private void TryPublishPhaseChanged(Entity matchEntity)
        {
            if (!_phaseChangedStash.Has(matchEntity))
                return;

            var phase = _phaseChangedStash.Get(matchEntity).Phase;
            _phaseChangedStash.Remove(matchEntity);
            _eventStream.PublishPhaseChanged(new BattleshipPhaseChangedEvent(phase));
        }

        private void TryPublishMarksChanged(Entity matchEntity)
        {
            if (!_marksChangedStash.Has(matchEntity))
                return;

            var payload = _marksChangedStash.Get(matchEntity);
            _marksChangedStash.Remove(matchEntity);
            
            _eventStream.PublishMarksChanged(
                payload.ViewerSlot,
                payload.SecondaryViewerSlot,
                payload.HasSecondaryViewer);
        }
    }
}