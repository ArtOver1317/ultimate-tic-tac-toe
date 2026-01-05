using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.Localization;
using UnityEngine;
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
        [SerializeField] private UIDocument UIDocument;
        [SerializeField] private bool _showInProduction = false;

        private ILocalizationService _localization;
        private CancellationTokenSource _cts;
        private CompositeDisposable _disposables;

        [Inject]
        public void Construct(ILocalizationService localization) => _localization = localization;

        private void OnEnable()
        {
#if !UNITY_EDITOR
            if (!_showInProduction)
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
            
            // Find or create our container (don't destroy existing UI!)
            const string containerName = "locale-debug-overlay";
            var container = root.Q<VisualElement>(containerName);
            
            if (container != null)
            {
                // Already exists, just update it
                container.Clear();
            }
            else
            {
                // Create new container
                container = new VisualElement
                {
                    name = containerName,
                };
                
                root.Add(container);
            }
            
            // Setup container styles
            container.style.position = Position.Absolute;
            container.style.top = 10;
            container.style.right = 10;
            container.style.backgroundColor = new Color(0, 0, 0, 0.8f);
            container.style.paddingTop = 10;
            container.style.paddingBottom = 10;
            container.style.paddingLeft = 10;
            container.style.paddingRight = 10;
            container.style.borderBottomLeftRadius = 5;
            container.style.borderBottomRightRadius = 5;
            container.style.borderTopLeftRadius = 5;
            container.style.borderTopRightRadius = 5;
            
            // Inner content
            var content = new VisualElement();

            // Title
            var title = new Label("Locale Debug")
            {
                style =
                {
                    color = Color.white,
                    fontSize = 14,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    marginBottom = 5,
                },
            };
            
            content.Add(title);

            // Current locale label
            var currentLocaleLabel = new Label("Current: Loading...")
            {
                style =
                {
                    color = Color.yellow,
                    fontSize = 12,
                    marginBottom = 10,
                },
            };
            
            content.Add(currentLocaleLabel);

            // Subscribe to locale changes
            _localization?.CurrentLocale
                .Subscribe(locale => currentLocaleLabel.text = $"Current: {locale.Code}")
                .AddTo(_disposables);

            // Buttons
            AddLocaleButton(content, "EN", LocaleId.EnglishUs);
            AddLocaleButton(content, "RU", LocaleId.Russian);
            AddLocaleButton(content, "JA", LocaleId.Japanese);

            container.Add(content);
        }

        private void AddLocaleButton(VisualElement parent, string label, LocaleId locale)
        {
            var button = new Button(() => OnLocaleButtonClicked(locale))
            {
                text = label,
                style =
                {
                    marginTop = 2,
                    paddingTop = 5,
                    paddingBottom = 5,
                    paddingLeft = 15,
                    paddingRight = 15,
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