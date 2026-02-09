#nullable enable

namespace Runtime.GameModes.Wizard
{
    /// <summary>
    /// Metadata describing an available game in the catalog.
    /// Used by game selection UI to render a list of games.
    /// </summary>
    public sealed class GameMetadata
    {
        public string Id { get; }
        public string DisplayNameKey { get; }
        public string DescriptionKey { get; }
        public string IconAssetKey { get; }
        public int SortOrder { get; }
        public bool SupportsBot { get; }
        public bool SupportsOnline { get; }
        public bool SupportsLocal { get; }

        public GameMetadata(
            string id,
            string displayNameKey,
            string descriptionKey,
            string iconAssetKey,
            int sortOrder,
            bool supportsBot,
            bool supportsOnline,
            bool supportsLocal)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new System.ArgumentException("Value cannot be null or whitespace.", nameof(id));
            
            if (string.IsNullOrWhiteSpace(displayNameKey))
                throw new System.ArgumentException("Value cannot be null or whitespace.", nameof(displayNameKey));
            
            if (string.IsNullOrWhiteSpace(descriptionKey))
                throw new System.ArgumentException("Value cannot be null or whitespace.", nameof(descriptionKey));
            
            if (string.IsNullOrWhiteSpace(iconAssetKey))
                throw new System.ArgumentException("Value cannot be null or whitespace.", nameof(iconAssetKey));

            Id = id;
            DisplayNameKey = displayNameKey;
            DescriptionKey = descriptionKey;
            IconAssetKey = iconAssetKey;
            SortOrder = sortOrder;
            SupportsBot = supportsBot;
            SupportsOnline = supportsOnline;
            SupportsLocal = supportsLocal;
        }
    }
}