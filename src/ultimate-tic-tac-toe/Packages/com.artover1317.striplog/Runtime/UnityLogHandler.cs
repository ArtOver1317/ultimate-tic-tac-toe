using UnityEngine;
using Object = UnityEngine.Object;

namespace StripLog
{
    public sealed class UnityLogHandler : ILogHandler
    {
        public void Log(LogLevel level, string tag, string message, Object context = null)
        {
            var formatted = string.IsNullOrEmpty(tag) ? message : $"[{tag}] {message}";

            switch (level)
            {
                case LogLevel.Debug:
                case LogLevel.Info:
                    Debug.Log(formatted, context);
                    break;
                case LogLevel.Warning:
                    Debug.LogWarning(formatted, context);
                    break;
                case LogLevel.Error:
                    Debug.LogError(formatted, context);
                    break;
                default:
                    Debug.Log(formatted, context);
                    break;
            }
        }
    }
}