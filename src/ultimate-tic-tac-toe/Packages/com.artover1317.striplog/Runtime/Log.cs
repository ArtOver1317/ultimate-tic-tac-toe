using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using UnityEngine;
using Object = UnityEngine.Object;

namespace StripLog
{
    public static partial class Log
    {
        private static ILogHandler _handler = new UnityLogHandler();
        private static volatile LogLevel _minLevel = LogLevel.Debug;

        private static readonly object _muteLock = new();
        private static volatile HashSet<string> _mutedTags = new(StringComparer.Ordinal);

        public static ILogHandler Handler
        {
            get => Volatile.Read(ref _handler);
            set => Interlocked.Exchange(ref _handler, value ?? new UnityLogHandler());
        }

        public static LogLevel MinLevel
        {
            get => _minLevel;
            set => _minLevel = value;
        }

        public static void MuteTag(string tag)
        {
            if (string.IsNullOrEmpty(tag))
                return;

            lock (_muteLock)
            {
                var copy = new HashSet<string>(_mutedTags, StringComparer.Ordinal)
                {
                    tag
                };

                _mutedTags = copy;
            }
        }

        public static void UnmuteTag(string tag)
        {
            if (string.IsNullOrEmpty(tag))
                return;

            lock (_muteLock)
            {
                var copy = new HashSet<string>(_mutedTags, StringComparer.Ordinal);
                copy.Remove(tag);
                _mutedTags = copy;
            }
        }

        public static bool IsTagMuted(string tag)
        {
            if (string.IsNullOrEmpty(tag))
                return false;

            return _mutedTags.Contains(tag);
        }

        static partial void BroadcastLog(LogLevel level, string tag, string message);

#if UNITY_INCLUDE_TESTS
        internal static void ResetForTests()
        {
            Handler = new UnityLogHandler();
            MinLevel = LogLevel.Debug;

            lock (_muteLock)
            {
                _mutedTags = new HashSet<string>(StringComparer.Ordinal);
            }
        }
#endif

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD"), Conditional("FORCE_LOGS")]
        public static void Debug(string tag, string message, Object context = null)
            => TryLogStripped(LogLevel.Debug, tag, message, context);

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD"), Conditional("FORCE_LOGS")]
        public static void Debug(string tag, Func<string> messageFactory, Object context = null)
            => TryLogStripped(LogLevel.Debug, tag, messageFactory, context);

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD"), Conditional("FORCE_LOGS")]
        public static void Info(string tag, string message, Object context = null)
            => TryLogStripped(LogLevel.Info, tag, message, context);

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD"), Conditional("FORCE_LOGS")]
        public static void Info(string tag, Func<string> messageFactory, Object context = null)
            => TryLogStripped(LogLevel.Info, tag, messageFactory, context);

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD"), Conditional("FORCE_LOGS")]
        public static void Warning(string tag, string message, Object context = null)
            => TryLogStripped(LogLevel.Warning, tag, message, context);

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD"), Conditional("FORCE_LOGS")]
        public static void Warning(string tag, Func<string> messageFactory, Object context = null)
            => TryLogStripped(LogLevel.Warning, tag, messageFactory, context);

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD"), Conditional("FORCE_LOGS")]
        public static void ErrorDev(string tag, string message, Object context = null)
            => TryLogStripped(LogLevel.Error, tag, message, context);

        public static void Error(string tag, string message, Object context = null)
            => LogAlways(LogLevel.Error, tag, message, context);

        public static void Exception(Exception ex, string tag = null)
        {
            if (ex == null)
                return;

            if (ex is OperationCanceledException)
            {
                LogAlways(LogLevel.Info, tag ?? "Cancellation", ex.ToString(), context: null);
                return;
            }

            LogAlways(LogLevel.Error, tag ?? "Exception", ex.ToString(), context: null);
        }

        private static void TryLogStripped(LogLevel level, string tag, string message, Object context)
        {
            if (!IsEnabledForStripped(level, tag))
                return;

            var handler = Volatile.Read(ref _handler) ?? new UnityLogHandler();
            handler.Log(level, tag, message ?? string.Empty, context);
            BroadcastLog(level, tag, message ?? string.Empty);
        }

        private static void TryLogStripped(LogLevel level, string tag, Func<string> messageFactory, Object context)
        {
            if (!IsEnabledForStripped(level, tag))
                return;

            var message = messageFactory?.Invoke() ?? string.Empty;

            var handler = Volatile.Read(ref _handler) ?? new UnityLogHandler();
            handler.Log(level, tag, message, context);
            BroadcastLog(level, tag, message);
        }

        private static bool IsEnabledForStripped(LogLevel level, string tag)
        {
            if (level < _minLevel)
                return false;

            if (IsTagMuted(tag))
                return false;

            return true;
        }

        private static void LogAlways(LogLevel level, string tag, string message, Object context)
        {
            var handler = Volatile.Read(ref _handler) ?? new UnityLogHandler();
            handler.Log(level, tag, message ?? string.Empty, context);
            BroadcastLog(level, tag, message ?? string.Empty);
        }
    }
}