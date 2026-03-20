using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.Localization;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UIElements;
using VContainer;

namespace Runtime.UI.Debugging
{
    /// <summary>
    /// Simple debug UI overlay for switching locales in runtime.
    /// Attach to any GameObject with UIDocument in the scene.
    /// </summary>
    public class LocaleDebugOverlay : MonoBehaviour
    {
        private const string _containerName = "locale-debug-overlay";
        private const float _backgroundAlpha = 0.8f;
        private const int _containerOffset = 10;
        private const int _containerPadding = 10;
        private const int _borderRadius = 5;
        private const int _titleFontSize = 14;
        private const int _titleMarginBottom = 5;
        private const int _localeFontSize = 12;
        private const int _localeMarginBottom = 10;
        private const int _buttonMarginTop = 2;
        private const int _buttonVerticalPadding = 5;
        private const int _buttonHorizontalPadding = 15;

        [SerializeField] private UIDocument UIDocument;
        
        [FormerlySerializedAs("_showInProduction")]
        [SerializeField] private bool ShowInProduction;

        private ILocalizationService _localization;
        private CancellationTokenSource _cts;
        private CompositeDisposable _disposables;

        [Inject]
        public void Construct(ILocalizationService localization) => _localization = localization;

        private void OnEnable()
        {
#if UNITY_EDITOR
            // Suppress CS0414 in editor builds: the field is used only in non-editor builds.
            _ = ShowInProduction;
#else
            if (!ShowInProduction)
            {
                gameObject.SetActive(false);
                return;
            }
#endif

            if (UIDocument == null)
                UIDocument = GetComponent<UIDocument>();

            if (UIDocument == null)
            {
                Debug.LogError("[LocaleDebugOverlay] UIDocument not found!");
                return;
            }

            _cts = new CancellationTokenSource();
            _disposables = new CompositeDisposable();
            CreateDebugUI();
        }

        private void OnDisable()
        {
            _disposables?.Dispose();
            _disposables = null;
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }

        private void CreateDebugUI()
        {
            var root = UIDocument.rootVisualElement;
            var container = GetOrCreateContainer(root);

            ApplyContainerStyles(container);
            container.Add(CreateContent());
        }

        private VisualElement GetOrCreateContainer(VisualElement root)
        {
            var container = root.Q<VisualElement>(_containerName);

            if (container != null)
            {
                container.Clear();
                return container;
            }

            container = new VisualElement
            {
                name = _containerName,
            };

            root.Add(container);
            return container;
        }

        private void ApplyContainerStyles(VisualElement container)
        {
            container.style.position = Position.Absolute;
            container.style.top = _containerOffset;
            container.style.right = _containerOffset;
            container.style.backgroundColor = new Color(0, 0, 0, _backgroundAlpha);
            container.style.paddingTop = _containerPadding;
            container.style.paddingBottom = _containerPadding;
            container.style.paddingLeft = _containerPadding;
            container.style.paddingRight = _containerPadding;
            container.style.borderBottomLeftRadius = _borderRadius;
            container.style.borderBottomRightRadius = _borderRadius;
            container.style.borderTopLeftRadius = _borderRadius;
            container.style.borderTopRightRadius = _borderRadius;
        }

        private VisualElement CreateContent()
        {
            var content = new VisualElement();
            var currentLocaleLabel = CreateCurrentLocaleLabel();

            content.Add(CreateTitleLabel());
            content.Add(currentLocaleLabel);

            BindCurrentLocaleLabel(currentLocaleLabel);
            AddLocaleButtons(content);
            return content;
        }

        private static Label CreateTitleLabel() => new("Locale Debug")
        {
            style =
            {
                color = Color.white,
                fontSize = _titleFontSize,
                unityFontStyleAndWeight = FontStyle.Bold,
                marginBottom = _titleMarginBottom,
            },
        };

        private Label CreateCurrentLocaleLabel() =>
            new("Current: Loading...")
            {
                style =
                {
                    color = Color.yellow,
                    fontSize = _localeFontSize,
                    marginBottom = _localeMarginBottom,
                },
            };

        private void BindCurrentLocaleLabel(Label currentLocaleLabel) =>
            _localization?.CurrentLocale
                .Subscribe(locale => currentLocaleLabel.text = $"Current: {locale.Code}")
                .AddTo(_disposables);

        private void AddLocaleButtons(VisualElement content)
        {
            AddLocaleButton(content, "EN", LocaleId.EnglishUs);
            AddLocaleButton(content, "RU", LocaleId.Russian);
            AddLocaleButton(content, "JA", LocaleId.Japanese);
        }

        private void AddLocaleButton(VisualElement parent, string label, LocaleId locale)
        {
            var button = new Button(() => OnLocaleButtonClicked(locale))
            {
                text = label,
                style =
                {
                    marginTop = _buttonMarginTop,
                    paddingTop = _buttonVerticalPadding,
                    paddingBottom = _buttonVerticalPadding,
                    paddingLeft = _buttonHorizontalPadding,
                    paddingRight = _buttonHorizontalPadding,
                },
            };
            
            parent.Add(button);
        }

        // Internal for testing without reflection
        internal void OnLocaleButtonClicked(LocaleId locale)
        {
            if (_localization == null)
            {
                Debug.LogError("[LocaleDebugOverlay] Localization service not available");
                return;
            }

            SwitchLocaleAsync(locale).Forget();
        }

        private async UniTaskVoid SwitchLocaleAsync(LocaleId locale)
        {
            try
            {
                await _localization.SetLocaleAsync(locale, _cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Expected during scene unload or disable
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LocaleDebugOverlay] Failed to switch locale: {ex.Message}");
            }
        }
    }
}