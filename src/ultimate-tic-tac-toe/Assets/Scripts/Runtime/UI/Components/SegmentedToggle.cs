#nullable enable

using System;
using UnityEngine.UIElements;

namespace Runtime.UI.Components
{
    [UxmlElement]
    public sealed partial class SegmentedToggle : VisualElement
    {
        private readonly Button _leftButton;
        private readonly Button _rightButton;
        private int _selectedIndex;

        public event Action<int>? SelectedIndexChanged;

        public int SelectedIndex => _selectedIndex;

        public SegmentedToggle()
        {
            AddToClassList("segmented-toggle");

            _leftButton = new Button { name = "LeftButton" };
            _rightButton = new Button { name = "RightButton" };

            _leftButton.AddToClassList("segmented-toggle__button");
            _rightButton.AddToClassList("segmented-toggle__button");

            _leftButton.clicked += () => SetSelectedIndexInternal(0, notify: true);
            _rightButton.clicked += () => SetSelectedIndexInternal(1, notify: true);

            Add(_leftButton);
            Add(_rightButton);

            UpdateVisualState();
        }

        public void SetLabels(string? leftLabel, string? rightLabel)
        {
            _leftButton.text = leftLabel ?? string.Empty;
            _rightButton.text = rightLabel ?? string.Empty;
        }

        public void SetSelectedIndex(int index) => SetSelectedIndexInternal(index, notify: true);

        public void SetSelectedIndexWithoutNotify(int index) => SetSelectedIndexInternal(index, notify: false);

        private void SetSelectedIndexInternal(int index, bool notify)
        {
            if (index is < 0 or > 1)
                throw new ArgumentOutOfRangeException(nameof(index), index, "SegmentedToggle supports only indices 0 or 1.");

            if (_selectedIndex == index)
                return;

            _selectedIndex = index;
            UpdateVisualState();

            if (notify)
                SelectedIndexChanged?.Invoke(index);
        }

        private void UpdateVisualState()
        {
            SetSelected(_leftButton, _selectedIndex == 0);
            SetSelected(_rightButton, _selectedIndex == 1);
        }

        private static void SetSelected(VisualElement element, bool selected)
        {
            const string SelectedClass = "segmented-toggle__button--selected";

            if (selected)
                element.AddToClassList(SelectedClass);
            else
                element.RemoveFromClassList(SelectedClass);
        }
    }
}

#nullable restore
