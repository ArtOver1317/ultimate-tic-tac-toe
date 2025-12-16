using System;
using StripLog;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Runtime.Infrastructure.Logging
{
    public static class GameLog
    {
        public static void Debug(string message, Object context = null)
            => Log.Debug(LogTags.Default, message, context);

        public static void Debug(Func<string> messageFactory, Object context = null)
            => Log.Debug(LogTags.Default, messageFactory, context);

        public static void Info(string message, Object context = null)
            => Log.Info(LogTags.Default, message, context);

        public static void Info(Func<string> messageFactory, Object context = null)
            => Log.Info(LogTags.Default, messageFactory, context);

        public static void Warning(string message, Object context = null)
            => Log.Warning(LogTags.Default, message, context);

        public static void Warning(Func<string> messageFactory, Object context = null)
            => Log.Warning(LogTags.Default, messageFactory, context);

        public static void Error(string message, Object context = null)
            => Log.Error(LogTags.Default, message, context);

        public static void ErrorDev(string message, Object context = null)
            => Log.ErrorDev(LogTags.Default, message, context);

        public static void Exception(Exception ex)
            => Log.Exception(ex, LogTags.Default);
    }
}
