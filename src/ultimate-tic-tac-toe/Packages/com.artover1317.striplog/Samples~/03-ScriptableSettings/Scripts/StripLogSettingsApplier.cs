using UnityEngine;

namespace StripLog.Samples.ScriptableSettings
{
    public sealed class StripLogSettingsApplier : MonoBehaviour
    {
        [SerializeField] private StripLogSettings _settings;
        [SerializeField] private bool _applyOnEnable = true;

        private void OnEnable()
        {
            if (!_applyOnEnable)
                return;

            if (_settings == null)
                return;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Log.MinLevel = _settings.MinLevel;

            if (_settings.MutedTags != null)
            {
                for (int i = 0; i < _settings.MutedTags.Count; i++)
                {
                    var tag = _settings.MutedTags[i];
                    Log.MuteTag(tag);
                }
            }
#endif
        }
    }
}
