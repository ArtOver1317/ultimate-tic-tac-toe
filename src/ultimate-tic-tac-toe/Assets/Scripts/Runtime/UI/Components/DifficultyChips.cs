#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Runtime.UI.Components
{
    public sealed class DifficultyChipItem
    {
        public string Id { get; }
        public string Label { get; }

        public DifficultyChipItem(string id, string? label)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(id));

            Id = id;
            Label = label ?? string.Empty;
        }
    }

    [UxmlElement]
    public sealed partial class DifficultyChips : VisualElement
    {
        private const string _itemClass = "difficulty-chips__item";
        private const string _selectedClass = "difficulty-chips__item--selected";
        private const string _lastItemClass = "difficulty-chips__item--last";

        private readonly List<Button> _orderedButtons = new();
        private readonly Dictionary<string, Button> _buttonsById = new(StringComparer.Ordinal);

        public event Action<string>? SelectedIdChanged;

        public string? SelectedId { get; private set; }

        public DifficultyChips()
        {
            AddToClassList("difficulty-chips");
        }

        public void SetItems(IReadOnlyList<DifficultyChipItem>? items)
        {
            Clear();
            _orderedButtons.Clear();
            _buttonsById.Clear();

            if (items == null || items.Count == 0)
            {
                SelectedId = null;
                UpdateVisualState();
                return;
            }

            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                
                if (item == null)
                    throw new ArgumentException("Items collection contains null entry.", nameof(items));

                if (_buttonsById.ContainsKey(item.Id))
                    throw new InvalidOperationException($"Duplicate difficulty id detected: '{item.Id}'.");

                var button = new Button { text = item.Label ?? string.Empty, name = item.Id };
                button.AddToClassList(_itemClass);

                var id = item.Id;
                button.clicked += () => SetSelectedIdInternal(id, notify: true);

                _orderedButtons.Add(button);
                _buttonsById.Add(id, button);
                Add(button);
            }

            UpdateLastItemClass();

            if (!string.IsNullOrWhiteSpace(SelectedId) && !_buttonsById.ContainsKey(SelectedId))
                SelectedId = null;

            UpdateVisualState();
        }

        public void SetSelectedId(string? id) => SetSelectedIdInternal(id, notify: true);

        public void SetSelectedIdWithoutNotify(string? id) => SetSelectedIdInternal(id, notify: false);

        public void SetLabel(string id, string? label)
        {
            if (string.IsNullOrWhiteSpace(id))
                return;

            if (_buttonsById.TryGetValue(id, out var button))
                button.text = label ?? string.Empty;
        }

        private void SetSelectedIdInternal(string? id, bool notify)
        {
            if (string.Equals(SelectedId, id, StringComparison.Ordinal))
                return;

            if (!string.IsNullOrWhiteSpace(id) && !_buttonsById.ContainsKey(id))
                id = null;

            SelectedId = id;
            UpdateVisualState();

            if (notify && !string.IsNullOrWhiteSpace(SelectedId))
                SelectedIdChanged?.Invoke(SelectedId);
        }

        private void UpdateVisualState()
        {
            foreach (var pair in _buttonsById)
            {
                var isSelected = string.Equals(pair.Key, SelectedId, StringComparison.Ordinal);
                
                if (isSelected)
                    pair.Value.AddToClassList(_selectedClass);
                else
                    pair.Value.RemoveFromClassList(_selectedClass);
            }
        }

        private void UpdateLastItemClass()
        {
            for (var i = 0; i < _orderedButtons.Count; i++)
            {
                _orderedButtons[i].RemoveFromClassList(_lastItemClass);
            }

            if (_orderedButtons.Count == 0)
                return;

            _orderedButtons[^1].AddToClassList(_lastItemClass);
        }
    }
}