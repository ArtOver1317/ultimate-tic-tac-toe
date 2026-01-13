namespace Runtime.GameModes.Wizard
{
    public sealed class DirectInviteConfig : IOpponentConfig
    {
        public string PlayerId { get; }

        public DirectInviteConfig(string playerId)
        {
            if (string.IsNullOrWhiteSpace(playerId))
                throw new System.ArgumentException("Value cannot be null or whitespace.", nameof(playerId));

            PlayerId = playerId;
        }
    }
}
