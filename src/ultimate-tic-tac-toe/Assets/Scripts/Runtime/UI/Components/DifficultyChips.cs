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

        public DifficultyChipItem(string id, string label)
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
        private const string ItemClass = "difficulty-chips__item";
        private const string SelectedClass = "difficulty-chips__item--selected";
        private const string LastItemClass = "difficulty-chips__item--last";

        private readonly List<Button> _orderedButtons = new();
        private readonly Dictionary<string, Button> _buttonsById = new(StringComparer.Ordinal);
        private string? _selectedId;

        public event Action<string>? SelectedIdChanged;

        public string? SelectedId => _selectedId;

        public DifficultyChips()
        {
            AddToClassList("difficulty-chips");
        }

        public void SetItems(IReadOnlyList<DifficultyChipItem> items)
        {
            Clear();
            _orderedButtons.Clear();
            _buttonsById.Clear();

            if (items == null || items.Count == 0)
            {
                _selectedId = null;
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
                button.AddToClassList(ItemClass);

                var id = item.Id;
                button.clicked += () => SetSelectedIdInternal(id, notify: true);

                _orderedButtons.Add(button);
                _buttonsById.Add(id, button);
                Add(button);
            }

            UpdateLastItemClass();

            UpdateVisualState();
        }

        public void SetSelectedId(string? id) => SetSelectedIdInternal(id, notify: true);

        public void SetSelectedIdWithoutNotify(string? id) => SetSelectedIdInternal(id, notify: false);

        public void SetLabel(string id, string label)
        {
            if (string.IsNullOrWhiteSpace(id))
                return;

            if (_buttonsById.TryGetValue(id, out var button))
                button.text = label ?? string.Empty;
        }

        private void SetSelectedIdInternal(string? id, bool notify)
        {
            if (string.Equals(_selectedId, id, StringComparison.Ordinal))
                return;

            if (!string.IsNullOrWhiteSpace(id) && !_buttonsById.ContainsKey(id))
                id = null;

            _selectedId = id;
            UpdateVisualState();

            if (notify && !string.IsNullOrWhiteSpace(_selectedId))
                SelectedIdChanged?.Invoke(_selectedId);
        }

        private void UpdateVisualState()
        {
            foreach (var pair in _buttonsById)
            {
                var isSelected = string.Equals(pair.Key, _selectedId, StringComparison.Ordinal);
                if (isSelected)
                    pair.Value.AddToClassList(SelectedClass);
                else
                    pair.Value.RemoveFromClassList(SelectedClass);
            }
        }

        private void UpdateLastItemClass()
        {
            for (var i = 0; i < _orderedButtons.Count; i++)
                _orderedButtons[i].RemoveFromClassList(LastItemClass);

            if (_orderedButtons.Count == 0)
                return;

            _orderedButtons[^1].AddToClassList(LastItemClass);
        }
    }
}

#nullable restore