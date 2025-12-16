using System.Collections.Generic;
using UnityEngine;

namespace StripLog.Samples.ScriptableSettings
{
    [CreateAssetMenu(menuName = "StripLog/Settings", fileName = "StripLogSettings")]
    public sealed class StripLogSettings : ScriptableObject
    {
        public LogLevel MinLevel = LogLevel.Debug;
        public List<string> MutedTags = new List<string>();
    }
}
