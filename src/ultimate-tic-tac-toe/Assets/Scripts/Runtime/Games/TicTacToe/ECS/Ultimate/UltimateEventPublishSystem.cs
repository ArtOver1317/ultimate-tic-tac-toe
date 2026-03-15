using Runtime.Gameplay.ECS.Components;
using Runtime.Games.TicTacToe.Ultimate;
using Scellecs.Morpeh;

namespace Runtime.Games.TicTacToe.ECS
{
    /// <summary>
    /// Publishes ultimate gameplay events from one-shot ECS components to the event stream.
    /// Runs after the main state-update systems via RegisterPostPublishSystems.
    /// </summary>
    public sealed class UltimateEventPublishSystem : ISystem
    {
        private readonly UltimateGameplayEventStream _eventStream;

        private Filter _matchFilter;
        private Stash<UltimateAllowedMajorsChangedOneShot> _allowedStash;
        private Stash<UltimateMiniBoardStatusChangedOneShot> _miniBoardStash;

        public UltimateEventPublishSystem(UltimateGameplayEventStream eventStream) =>
            _eventStream = eventStream ?? throw new System.ArgumentNullException(nameof(eventStream));

        public World World { get; set; }

        public void OnAwake()
        {
            _matchFilter = World.Filter.With<MatchTag>().Build();
            _allowedStash = World.GetStash<UltimateAllowedMajorsChangedOneShot>();
            _miniBoardStash = World.GetStash<UltimateMiniBoardStatusChangedOneShot>();
        }

        public void OnUpdate(float deltaTime)
        {
            if (_matchFilter.IsEmpty())
                return;

            var matchEntity = _matchFilter.First();
            TryPublishAllowedMajorsChanged(matchEntity);
            TryPublishMiniBoardStatusChanged(matchEntity);
        }

        public void Dispose() { }

        private void TryPublishAllowedMajorsChanged(Entity matchEntity)
        {
            if (!_allowedStash.Has(matchEntity))
                return;

            ref var changed = ref _allowedStash.Get(matchEntity);
            var evt = new AllowedMajorsChangedEvent(changed.Epoch, changed.AllowedMajors);
            _allowedStash.Remove(matchEntity);
            _eventStream.PublishAllowedMajorsChanged(evt);
        }

        private void TryPublishMiniBoardStatusChanged(Entity matchEntity)
        {
            if (!_miniBoardStash.Has(matchEntity))
                return;

            ref var changed = ref _miniBoardStash.Get(matchEntity);
            var evt = new MiniBoardStatusChangedEvent(changed.Epoch, changed.Major, changed.NewStatus);
            _miniBoardStash.Remove(matchEntity);
            _eventStream.PublishMiniBoardStatusChanged(evt);
        }
    }
}