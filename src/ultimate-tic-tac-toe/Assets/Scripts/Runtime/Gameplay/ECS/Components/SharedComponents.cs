#nullable enable

using Runtime.Gameplay.Shared;
using Runtime.Gameplay;
using Scellecs.Morpeh;
using EcsGameStatus = Runtime.Gameplay.Shared.EcsGameStatus;

namespace Runtime.Gameplay.ECS.Components
{
    public struct MatchTag : IComponent { }

    public struct GameIdComponent : IComponent
    {
        public string Value;
    }

    public struct MatchStatusComponent : IComponent
    {
        public EcsGameStatus Status;
        public int? WinnerSlot;
        public EcsWinLine? WinLine;
    }

    public struct PlayersComponent : IComponent
    {
        public int PlayerCount;
        public int[] PlayerSlots;
        public int ActivePlayerSlot;
    }

    public struct FieldConfigComponent : IComponent
    {
        public FieldKind Kind;
        public int OuterSize;
        public int InnerSize;
    }

    public struct LastMoveComponent : IComponent
    {
        public bool HasValue;
        public CellId CellId;
    }

    public struct CommandSequenceComponent : IComponent
    {
        public long Value;
    }
}
