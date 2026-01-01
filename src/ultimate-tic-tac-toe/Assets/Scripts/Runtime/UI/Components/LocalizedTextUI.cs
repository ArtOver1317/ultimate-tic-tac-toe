using System;
using System.Collections.Generic;
using R3;
using Runtime.Localization;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace Runtime.UI.Components
{
    /// <summary>
    /// MonoBehaviour компонент для автоматической локализации текста в UI Toolkit.
    /// Можно использовать в инспекторе для быстрой локализации статических элементов.
    /// Автоматически подписывается на изменения локали и обновляет текст.
    /// </summary>
    public sealed class LocalizedTextUI : MonoBehaviour
    {
        [SerializeField] private string _table = "UI";
        [SerializeField] private string _key;
        [SerializeField] private UIDocument _uiDocument;
        [SerializeField] private string _targetElementName;

        private ILocalizationService _localization;
        private IDisposable _subscription;
        private Label _targetLabel;

        [Inject]
        public void Construct(ILocalizationService localization)
        {
            if (localization == null)
                throw new ArgumentNullException(nameof(localization));
            
            _localization = localization;
        }

        private void OnEnable()
        {
            if (_localization == null)
            {
                Debug.LogError($"[LocalizedTextUI] Localization service not injected on {gameObject.name}. Ensure VContainer is configured correctly.");
                return;
            }

            if (_uiDocument == null)
            {
                Debug.LogError($"[LocalizedTextUI] UIDocument not assigned on {gameObject.name}");
                return;
            }

            if (string.IsNullOrWhiteSpace(_table))
            {
                Debug.LogError($"[LocalizedTextUI] Table is empty or whitespace on {gameObject.name}");
                return;
            }

            if (string.IsNullOrWhiteSpace(_key))
            {
                Debug.LogError($"[LocalizedTextUI] Key is empty or whitespace on {gameObject.name}");
                return;
            }

            if (string.IsNullOrEmpty(_targetElementName))
            {
                Debug.LogError($"[LocalizedTextUI] Target element name is empty on {gameObject.name}");
                return;
            }

            _targetLabel = _uiDocument.rootVisualElement.Q<Label>(_targetElementName);
            if (_targetLabel == null)
            {
                Debug.LogError($"[LocalizedTextUI] Label with name '{_targetElementName}' not found in UIDocument on {gameObject.name}");
                return;
            }

            BindToLocalization();
        }

        private void OnDisable() => UnbindFromLocalization();

        private void OnDestroy() => UnbindFromLocalization();

        /// <summary>
        /// Динамическая установка ключа в runtime.
        /// </summary>
        public void SetKey(string table, string key)
        {
            if (string.IsNullOrWhiteSpace(table))
                throw new ArgumentNullException(nameof(table));
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentNullException(nameof(key));

            _table = table;
            _key = key;

            if (isActiveAndEnabled && _targetLabel != null)
            {
                UnbindFromLocalization();
                BindToLocalization();
            }
        }

        /// <summary>
        /// Динамическая установка ключа с аргументами в runtime.
        /// </summary>
        public void SetKey(string table, string key, IReadOnlyDictionary<string, object> args)
        {
            if (string.IsNullOrWhiteSpace(table))
                throw new ArgumentNullException(nameof(table));
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentNullException(nameof(key));

            _table = table;
            _key = key;

            if (isActiveAndEnabled && _targetLabel != null)
            {
                UnbindFromLocalization();
                BindToLocalization(args);
            }
        }

        private void BindToLocalization(IReadOnlyDictionary<string, object> args = null)
        {
            var tableId = new TextTableId(_table);
            var textKey = new TextKey(_key);

            try
            {
                _subscription = _localization
                    .Observe(tableId, textKey, args)
                    .Subscribe(text =>
                    {
                        // Show placeholder if translation is missing
                        _targetLabel.text = string.IsNullOrEmpty(text) 
                            ? $"[{_table}.{_key}]" 
                            : text;
                    });
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LocalizedTextUI] Failed to bind localization for key '{_key}' on {gameObject.name}: {ex}");
            }
        }

        private void UnbindFromLocalization()
        {
            _subscription?.Dispose();
            _subscription = null;
        }
    }
}