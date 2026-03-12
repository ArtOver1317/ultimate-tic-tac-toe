namespace Runtime.Gameplay.Shared
{
    public enum GameplayRejectionReason
    {
        Unknown = 0,
        MatchNotActive = 1,
        RoundAlreadyEnded = 2,
        CellOccupied = 3,
        NotPlayersTurn = 4,
        InvalidCell = 5,
        ForbiddenMove = 6,
    }

    public readonly struct CommandRejection
    {
        public GameplayRejectionReason Reason { get; }
        public CommandRejection(GameplayRejectionReason reason) => Reason = reason;

        public override string ToString() => $"CommandRejection({Reason})";
    }
}