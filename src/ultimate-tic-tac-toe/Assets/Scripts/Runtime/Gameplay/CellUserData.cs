namespace Runtime.Gameplay
{
    public sealed class CellUserData
    {
        public CellId CellId { get; }

        public CellUserData(CellId cellId) => CellId = cellId;

        public override string ToString() => CellId.ToString();
    }
}