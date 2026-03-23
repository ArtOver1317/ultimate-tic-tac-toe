using Runtime.Services.UI;
using Runtime.UI.Common;
using UnityEngine;
using VContainer;

namespace Runtime.Infrastructure.EntryPoint
{
    /// <summary>
    /// Persists the scene-placed UIBackgroundView across scene loads and adopts it into UIService
    /// so the background is visible from the very first frame without waiting for Addressables.
    /// 
    /// Setup:
    ///   1. Add UIBackgroundView prefab instance to the EntryPoint scene with ShowOnAwake = true.
    ///   2. Assign it to the Background field of this component (also on EntryPoint scene).
    ///   3. Register this component in GameLifetimeScope.
    /// </summary>
    public sealed class UIWindowBootstrapper : MonoBehaviour
    {
        [SerializeField] private UIBackgroundView Background;

        private void Awake()
        {
            if (Background != null)
                DontDestroyOnLoad(Background.gameObject);
        }

        [Inject]
        public void Construct(IUIService uiService)
        {
            if (Background == null)
                return;

            uiService.AdoptSceneWindow<UIBackgroundView, UIBackgroundViewModel>(Background);
        }
    }
}
