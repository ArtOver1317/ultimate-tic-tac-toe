using Runtime.Gameplay.ECS.Components;
using Runtime.Gameplay.Shared;
using Runtime.Infrastructure.Logging;
using Scellecs.Morpeh;
using StripLog;

namespace Runtime.Gameplay.ECS.Pipeline
{
    /// <summary>
    /// Infrastructure terminal system for timeout transitions.
    /// Consumes <see cref="TimeoutRequest"/> and writes terminal <see cref="MatchStatusComponent"/> + <see cref="RoundFinishedOneShot"/>.
    /// </summary>
    public sealed class TimeoutTerminalSystem : ISystem
    {
        public World World { get; set; }

        private Filter _matchFilter;
        private Stash<TimeoutRequest> _timeoutStash;
        private Stash<MatchStatusComponent> _statusStash;
        private Stash<PlayersComponent> _playersStash;
        private Stash<RoundFinishedOneShot> _roundFinishedStash;

        public void OnAwake()
        {
            _matchFilter = World.Filter.With<MatchTag>().With<TimeoutRequest>().Build();
            _timeoutStash = World.GetStash<TimeoutRequest>();
            _statusStash = World.GetStash<MatchStatusComponent>();
            _playersStash = World.GetStash<PlayersComponent>();
            _roundFinishedStash = World.GetStash<RoundFinishedOneShot>();
        }

        public void OnUpdate(float deltaTime)
        {
            foreach (var entity in _matchFilter)
            {
                ref var timeout = ref _timeoutStash.Get(entity);

                if (!_statusStash.Has(entity) || !_playersStash.Has(entity))
                {
                    _timeoutStash.Remove(entity);
                    continue;
                }

                ref var status = ref _statusStash.Get(entity);
                
                if (status.Status != GameStatus.InProgress)
                {
                    _timeoutStash.Remove(entity);
                    continue;
                }

                ref var players = ref _playersStash.Get(entity);
                
                if (players.PlayerCount != PlayerSlotMapping.PlayerCount
                    || players.PlayerSlots is not { Length: PlayerSlotMapping.PlayerCount })
                {
                    Log.Error(LogTags.Infrastructure,
                        $"[TimeoutTerminalSystem] Unsupported players layout for timeout resolution. PlayerCount={players.PlayerCount}, SlotsLength={players.PlayerSlots?.Length ?? 0}.");
                    
                    _timeoutStash.Remove(entity);
                    continue;
                }

                if (!Contains(players.PlayerSlots, timeout.LoserSlot))
                {
                    Log.Error(LogTags.Infrastructure,
                        $"[TimeoutTerminalSystem] Invalid LoserSlot={timeout.LoserSlot}. Timeout ignored.");
                    
                    _timeoutStash.Remove(entity);
                    continue;
                }

                var winnerSlot = FirstNonLoser(players.PlayerSlots, timeout.LoserSlot);
                
                if (!winnerSlot.HasValue)
                {
                    Log.Error(LogTags.Infrastructure,
                        $"[TimeoutTerminalSystem] Winner slot could not be resolved for LoserSlot={timeout.LoserSlot}. Timeout ignored.");
                    
                    _timeoutStash.Remove(entity);
                    continue;
                }

                status.Status = GameStatus.Timeout;
                status.WinnerSlot = winnerSlot;
                status.WinLine = null;

                _roundFinishedStash.Set(entity, new RoundFinishedOneShot
                {
                    Status = GameStatus.Timeout,
                    WinnerSlot = winnerSlot,
                    WinLine = null,
                });

                _timeoutStash.Remove(entity);
            }
        }

        public void Dispose() { }

        private static bool Contains(int[] slots, int value)
        {
            if (slots == null)
                return false;

            foreach (var slot in slots)
            {
                if (slot == value)
                    return true;
            }

            return false;
        }

        private static int? FirstNonLoser(int[] slots, int loserSlot)
        {
            if (slots == null)
                return null;

            foreach (var slot in slots)
            {
                if (slot != loserSlot)
                    return slot;
            }

            return null;
        }
    }
}