using UnityEngine;

namespace StripLog.Samples.BasicTags
{
    public sealed class BasicTagsExample : MonoBehaviour
    {
        [ContextMenu("Log Example")]
        private void LogExample()
        {
            Log.Info(LogTags.UI, "Button clicked", this);
            Log.Warning(LogTags.Network, "High latency detected", this);
            Log.Error(LogTags.Game, "Something went wrong", this);
        }
    }
}
