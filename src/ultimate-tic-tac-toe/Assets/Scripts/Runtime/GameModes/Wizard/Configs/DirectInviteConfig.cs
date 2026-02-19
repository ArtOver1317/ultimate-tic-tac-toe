namespace Runtime.GameModes.Wizard
{
    public sealed class DirectInviteConfig : IOpponentConfig
    {
        public string SessionId { get; }

        public DirectInviteConfig(string sessionId)
        {
            if (!OnlineSessionIdFormatter.TryNormalizeToCanonical(sessionId, out var canonical))
                throw new System.ArgumentException("SessionId must be a valid canonical invite code.", nameof(sessionId));

            SessionId = canonical;
        }
    }
}