#nullable enable

// NOTE: CellId currently lives in Runtime.Games.TicTacToe.Moves because it was created
// for TicTacToe. It's a generic Major/Minor coordinate that works for any grid game.
// If a second game needs CellId, move it to Runtime.Gameplay (YAGNI until then).
using Runtime.Gameplay.Shared;
using Runtime.Games.TicTacToe.Moves;
using Scellecs.Morpeh;

namespace Runtime.Gameplay.ECS.Components
{
    public struct MatchTag : IComponent { }

    public struct GameIdComponent : IComponent
    {
        public string Value;
    }

    public struct MatchStatusComponent : IComponent
    {
        public GameStatus Status;
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
