#if LOG_R3_SUPPORT
using R3;

namespace StripLog
{
    public readonly struct LogEntry
    {
        public readonly LogLevel Level;
        public readonly string Tag;
        public readonly string Message;

        public LogEntry(LogLevel level, string tag, string message)
        {
            Level = level;
            Tag = tag;
            Message = message;
        }
    }

    public static partial class Log
    {
        private static readonly Subject<LogEntry> _logSubject = new Subject<LogEntry>();
        public static Observable<LogEntry> OnLog
        {
            get { return _logSubject; }
        }

        static partial void BroadcastLog(LogLevel level, string tag, string message)
        {
            if (_logSubject.HasObservers)
                _logSubject.OnNext(new LogEntry(level, tag, message));
        }
    }
}
#endif
