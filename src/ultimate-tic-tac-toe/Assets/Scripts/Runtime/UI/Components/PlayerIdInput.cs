#nullable enable

using System;
using UnityEngine.UIElements;

namespace Runtime.UI.Components
{
    [UxmlElement]
    public sealed partial class PlayerIdInput : VisualElement
    {
        private const string _labelClass = "player-id-input__label";
        private const string _fieldClass = "player-id-input__field";
        private const string _errorClass = "player-id-input__error";

        private readonly Label _label;
        private readonly TextField _textField;
        private readonly Label _errorLabel;

        private bool _suppressNotify;

        public event Action<string>? ValueChanged;

        public string Value => _textField.value ?? string.Empty;

        public PlayerIdInput()
        {
            AddToClassList("player-id-input");

            _label = new Label { name = "TitleLabel" };
            _label.AddToClassList(_labelClass);
            Add(_label);

            _textField = new TextField { name = "InputField" };
            _textField.AddToClassList(_fieldClass);
            _textField.isDelayed = true;
            _textField.RegisterValueChangedCallback(OnValueChanged);
            Add(_textField);

            _errorLabel = new Label { name = "ErrorLabel" };
            _errorLabel.AddToClassList(_errorClass);
            _errorLabel.style.display = DisplayStyle.None;
            Add(_errorLabel);
        }

        public void SetLabel(string? text) => _label.text = text ?? string.Empty;

        public void SetValueWithoutNotify(string? value)
        {
            _suppressNotify = true;
            
            try
            {
                _textField.SetValueWithoutNotify(value ?? string.Empty);
            }
            finally
            {
                _suppressNotify = false;
            }
        }

        public void SetError(string? error)
        {
            _errorLabel.text = error ?? string.Empty;
            
            _errorLabel.style.display = string.IsNullOrWhiteSpace(error)
                ? DisplayStyle.None
                : DisplayStyle.Flex;
        }

        internal void SimulateInput(string? value)
        {
            _textField.SetValueWithoutNotify(value ?? string.Empty);

            using var evt = ChangeEvent<string>.GetPooled(string.Empty, value ?? string.Empty);
            OnValueChanged(evt);
        }

        private void OnValueChanged(ChangeEvent<string> evt)
        {
            if (_suppressNotify)
                return;

            ValueChanged?.Invoke(evt.newValue ?? string.Empty);
        }
    }
}