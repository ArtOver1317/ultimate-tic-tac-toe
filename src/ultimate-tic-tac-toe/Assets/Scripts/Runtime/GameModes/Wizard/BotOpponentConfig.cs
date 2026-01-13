namespace Runtime.GameModes.Wizard
{
    public sealed class BotOpponentConfig : IOpponentConfig
    {
        public string DifficultyId { get; }

        public BotOpponentConfig(string difficultyId)
        {
            if (string.IsNullOrWhiteSpace(difficultyId))
                throw new System.ArgumentException("Value cannot be null or whitespace.", nameof(difficultyId));

            DifficultyId = difficultyId;
        }
    }
}
