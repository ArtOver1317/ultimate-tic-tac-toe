#nullable enable

using System;
using System.Collections.Generic;
using Runtime.GameModes.Wizard;
using Runtime.Infrastructure.Logging;
using UnityEngine.UIElements;

namespace Runtime.UI.Components
{
    public sealed class HumanKindRadioItem
    {
        public HumanOpponentKind Kind { get; }
        public string Label { get; }
        public bool IsEnabled { get; }

        public HumanKindRadioItem(HumanOpponentKind kind, string label, bool isEnabled = true)
        {
            Kind = kind;
            Label = label ?? string.Empty;
            IsEnabled = isEnabled;
        }
    }

    [UxmlElement]
    public sealed partial class HumanKindRadio : VisualElement
    {
        private const string ItemClass = "human-kind-radio__item";
        private const string SelectedClass = "human-kind-radio__item--selected";
        private const string LastItemClass = "human-kind-radio__item--last";
        private const string DisabledClass = "human-kind-radio__item--disabled";

        private readonly List<Button> _orderedButtons = new();
        private readonly Dictionary<HumanOpponentKind, Button> _buttonsByKind = new();
        private HumanOpponentKind? _selectedKind;

        public event Action<HumanOpponentKind>? SelectedKindChanged;

        public HumanOpponentKind? SelectedKind => _selectedKind;

        public HumanKindRadio()
        {
            AddToClassList("human-kind-radio");
        }

        public void SetItems(IReadOnlyList<HumanKindRadioItem> items)
        {
            Clear();
            _orderedButtons.Clear();
            _buttonsByKind.Clear();

            if (items == null || items.Count == 0)
            {
                _selectedKind = null;
                return;
            }

            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item == null)
                {
                    GameLog.Warning("[HumanKindRadio] Null item ignored.");
                    continue;
                }

                if (_buttonsByKind.ContainsKey(item.Kind))
                {
                    GameLog.Warning($"[HumanKindRadio] Duplicate human opponent kind '{item.Kind}' ignored.");
                    continue;
                }

                var button = new Button { text = item.Label ?? string.Empty, name = item.Kind.ToString() };
                button.AddToClassList(ItemClass);

                if (!item.IsEnabled)
                {
                    button.SetEnabled(false);
                    button.AddToClassList(DisabledClass);
                }

                var kind = item.Kind;
                button.clicked += () =>
                {
                    if (item.IsEnabled)
                        SetSelectedKindInternal(kind, notify: true);
                };

                _orderedButtons.Add(button);
                _buttonsByKind.Add(kind, button);
                Add(button);
            }

            UpdateLastItemClass();

            if (_selectedKind.HasValue && !_buttonsByKind.ContainsKey(_selectedKind.Value))
                _selectedKind = null;

            UpdateVisualState();
        }

        public void SetSelectedKind(HumanOpponentKind kind) => SetSelectedKindInternal(kind, notify: true);

        public void SetSelectedKindWithoutNotify(HumanOpponentKind kind) => SetSelectedKindInternal(kind, notify: false);

        private void SetSelectedKindInternal(HumanOpponentKind kind, bool notify)
        {
            if (!_buttonsByKind.ContainsKey(kind))
            {
                GameLog.Warning($"[HumanKindRadio] Unknown kind '{kind}' ignored.");
                return;
            }

            if (_selectedKind.HasValue && _selectedKind.Value.Equals(kind))
                return;

            _selectedKind = kind;
            UpdateVisualState();

            if (notify)
                SelectedKindChanged?.Invoke(kind);
        }

        private void UpdateVisualState()
        {
            foreach (var pair in _buttonsByKind)
            {
                var isSelected = _selectedKind.HasValue && pair.Key.Equals(_selectedKind.Value);
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
