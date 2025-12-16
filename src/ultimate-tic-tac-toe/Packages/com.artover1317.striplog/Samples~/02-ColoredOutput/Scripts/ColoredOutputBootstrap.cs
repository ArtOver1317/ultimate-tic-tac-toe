using UnityEngine;

namespace StripLog.Samples.ColoredOutput
{
    public sealed class ColoredOutputBootstrap : MonoBehaviour
    {
        [SerializeField] private bool _applyOnEnable = true;

        private void OnEnable()
        {
            if (!_applyOnEnable)
                return;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Log.Handler = new ColoredOutputLogHandler();
#endif
        }
    }
}
