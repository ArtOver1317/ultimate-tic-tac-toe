#nullable enable

namespace Runtime.GameModes.Wizard
{
    /// <summary>
    /// Metadata describing an available game mode.
    /// Used by mode selection UI to render a list of modes.
    /// </summary>
    public sealed class GameModeMetadata
    {
        public string Id { get; }
        public string DisplayNameKey { get; }
        public string DescriptionKey { get; }
        public string IconAssetKey { get; }
        public int SortOrder { get; }
        public bool SupportsBot { get; }
        public bool SupportsOnline { get; }
        public bool SupportsLocal { get; }
        public Gameplay.FieldKind FieldKind { get; }

        public GameModeMetadata(
            string id,
            string displayNameKey,
            string descriptionKey,
            string iconAssetKey,
            int sortOrder,
            bool supportsBot,
            bool supportsOnline,
            bool supportsLocal,
            Gameplay.FieldKind fieldKind)
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
            FieldKind = fieldKind;
        }
    }
}