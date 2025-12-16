using UnityEngine;
using StripLog;
using Object = UnityEngine.Object;

namespace StripLog.Samples.ColoredOutput
{
    public sealed class ColoredOutputLogHandler : ILogHandler
    {
        private readonly ILogHandler _inner;

        public ColoredOutputLogHandler() : this(new UnityLogHandler())
        {
        }

        public ColoredOutputLogHandler(ILogHandler inner)
        {
            _inner = inner ?? new UnityLogHandler();
        }

        public void Log(LogLevel level, string tag, string message, Object context = null)
        {
            var safeMessage = message ?? string.Empty;
            if (!string.IsNullOrEmpty(tag))
            {
                var colorName = GetColorName(level);
                tag = "<color=" + colorName + ">" + tag + "</color>";
            }

            _inner.Log(level, tag, safeMessage, context);
        }

        private static string GetColorName(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Debug:
                case LogLevel.Info:
                    return "cyan";
                case LogLevel.Warning:
                    return "yellow";
                case LogLevel.Error:
                    return "red";
                default:
                    return "white";
            }
        }
    }
}
