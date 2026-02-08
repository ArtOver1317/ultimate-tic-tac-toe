namespace Runtime.GameModes.Wizard
{
    public sealed class DirectInviteConfig : IOpponentConfig
    {
        public string PlayerId { get; }

        public DirectInviteConfig(string playerId)
        {
            if (!global::Runtime.GameModes.Wizard.PlayerId.TryCreate(playerId, out var parsed))
                throw new System.ArgumentException("PlayerId must be a numeric ulong.", nameof(playerId));

            if (parsed != null) 
                PlayerId = parsed.Value;
        }
    }
}