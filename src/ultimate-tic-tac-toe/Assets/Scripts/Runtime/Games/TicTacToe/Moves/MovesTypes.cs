namespace Runtime.Games.TicTacToe.Moves
{
    public enum PlayerMark
    {
        None = 0,
        X = 1,
        O = 2,
    }

    public readonly struct MovesVfxSettings
    {
        private const float _defaultMarkAppearDurationSeconds = 0.16f;

        public bool EnableMarkAppearAnimation { get; }
        public float MarkAppearDurationSeconds { get; }

        public static MovesVfxSettings Default => new(
            enableMarkAppearAnimation: true,
            markAppearDurationSeconds: _defaultMarkAppearDurationSeconds);

        public MovesVfxSettings(bool enableMarkAppearAnimation, float markAppearDurationSeconds)
        {
            EnableMarkAppearAnimation = enableMarkAppearAnimation;
            MarkAppearDurationSeconds = markAppearDurationSeconds;
        }
    }

    public readonly struct CellValue
    {
        public CellId CellId { get; }
        public PlayerMark Value { get; }

        public CellValue(CellId cellId, PlayerMark value)
        {
            CellId = cellId;
            Value = value;
        }
    }
}