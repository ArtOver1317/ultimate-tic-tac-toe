#nullable enable

using System;

namespace Runtime.GameModes.Wizard.Configs
{
    public sealed class BotDifficulty
    {
        public string Id { get; }
        public string NameKey { get; }
        public int SortOrder { get; }

        public BotDifficulty(string id, string nameKey, int sortOrder)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(id));
            
            if (string.IsNullOrWhiteSpace(nameKey))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(nameKey));
            
            if (sortOrder < 0)
                throw new ArgumentOutOfRangeException(nameof(sortOrder), sortOrder, "SortOrder must be non-negative.");

            Id = id;
            NameKey = nameKey;
            SortOrder = sortOrder;
        }
    }
}