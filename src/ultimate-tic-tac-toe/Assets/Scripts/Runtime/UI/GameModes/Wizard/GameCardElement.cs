#nullable enable

using Runtime.GameModes.Wizard;
using UnityEngine.UIElements;

namespace Runtime.UI.GameModes.Wizard
{
    /// <summary>
    /// Lightweight UI Toolkit element used by ModeSelection ListView virtualization.
    /// Represents a single mode card.
    /// </summary>
    public sealed class GameCardElement : VisualElement
    {
        public const string RootClass = "gmw-mode-card";
        public const string SelectedClass = "gmw-mode-card--selected";

        private readonly VisualElement _icon;
        private readonly Label _title;
        private readonly Label _description;

        public GameCardElement()
        {
            AddToClassList(RootClass);

            _icon = new VisualElement { name = "Icon" };
            _icon.AddToClassList("gmw-mode-card__icon");

            var textContainer = new VisualElement { name = "Text" };
            textContainer.AddToClassList("gmw-mode-card__text");

            _title = new Label { name = "Title" };
            _title.AddToClassList("gmw-mode-card__title");

            _description = new Label { name = "Description" };
            _description.AddToClassList("gmw-mode-card__description");

            textContainer.Add(_title);
            textContainer.Add(_description);

            Add(_icon);
            Add(textContainer);
        }

        public void Bind(GameMetadata? meta, bool isSelected)
        {
            if (meta == null)
            {
                Bind(title: string.Empty, description: string.Empty, iconKey: null, isSelected: false);
                return;
            }

            Bind(
                title: meta.DisplayNameKey,
                description: meta.DescriptionKey,
                iconKey: meta.IconAssetKey,
                isSelected: isSelected);
        }

        public void Bind(string? title, string? description, string? iconKey, bool isSelected)
        {
            _title.text = title ?? string.Empty;
            _description.text = description ?? string.Empty;

            // Icon asset loading is intentionally deferred to a later phase.
            // For now we expose the key via tooltip to ease debugging.
            _icon.tooltip = string.IsNullOrWhiteSpace(iconKey) ? string.Empty : iconKey;

            EnableInClassList(SelectedClass, isSelected);
        }
    }
}

#nullable restore
