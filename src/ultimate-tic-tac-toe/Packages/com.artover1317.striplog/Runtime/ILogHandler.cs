using UnityEngine;
using Object = UnityEngine.Object;

namespace StripLog
{
    public interface ILogHandler
    {
        void Log(LogLevel level, string tag, string message, Object context = null);
    }
}