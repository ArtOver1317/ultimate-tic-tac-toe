using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace StripLog.Tests
{
    internal sealed class RecordingLogHandler : ILogHandler
    {
        internal readonly List<Entry> Entries = new List<Entry>();

        public void Log(LogLevel level, string tag, string message, Object context = null)
            => Entries.Add(new Entry(level, tag, message, context));

        internal readonly struct Entry
        {
            internal readonly LogLevel Level;
            internal readonly string Tag;
            internal readonly string Message;
            internal readonly Object Context;

            internal Entry(LogLevel level, string tag, string message, Object context)
            {
                Level = level;
                Tag = tag;
                Message = message;
                Context = context;
            }
        }
    }
}
