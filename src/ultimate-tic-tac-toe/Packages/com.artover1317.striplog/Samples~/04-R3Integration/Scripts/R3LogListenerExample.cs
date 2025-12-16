#if LOG_R3_SUPPORT
using UnityEngine;
using R3;

namespace StripLog.Samples.R3Integration
{
    public sealed class R3LogListenerExample : MonoBehaviour
    {
        private IDisposable _subscription;

        private void OnEnable()
        {
            _subscription = Log.OnLog
                .Subscribe(entry => Debug.Log("[R3] " + entry.Level + " " + entry.Tag + ": " + entry.Message));
        }

        private void OnDisable()
        {
            if (_subscription != null)
            {
                _subscription.Dispose();
                _subscription = null;
            }
        }
    }
}
#endif
