using System;
using System.Collections.Generic;

namespace Runtime.Localization
{
    public enum LocalizationStoreEventType
    {
        TableLoaded = 0,
        TableUnloaded = 1,
        ActiveLocaleChanged = 2,
        LoadFailed = 3,
    }
    
    public readonly struct LocalizationStoreEvent
    {
        public LocalizationStoreEventType Type { get; }
        public LocaleId Locale { get; }
        public TextTableId TableId { get; }
        public string Details { get; }

        public LocalizationStoreEvent(LocalizationStoreEventType type, LocaleId locale, TextTableId tableId, string details)
        {
            Type = type;
            Locale = locale;
            TableId = tableId;
            Details = details ?? string.Empty;
        }
    }

    public enum LocalizationErrorCode
    {
        Unknown = 0,
        AddressablesLoadFailed = 1,
        ParseFailed = 2,
        MissingKey = 3,
        UnsupportedLocale = 4,
    }
    
    public readonly struct LocalizationError
    {
        public LocalizationErrorCode Code { get; }
        public string Message { get; }
        public Exception Exception { get; }
        public LocaleId? Locale { get; }
        public TextTableId? TableId { get; }
        public TextKey? Key { get; }

        public LocalizationError(
            LocalizationErrorCode code,
            string message,
            Exception exception = null,
            LocaleId? locale = null,
            TextTableId? tableId = null,
            TextKey? key = null)
        {
            Code = code;
            Message = message ?? string.Empty;
            Exception = exception;
            Locale = locale;
            TableId = tableId;
            Key = key;
        }
    }
    
    public sealed class LocalizationTable
    {
        private readonly IReadOnlyDictionary<string, string> _entries;

        public LocaleId Locale { get; }
        public TextTableId TableId { get; }

        public LocalizationTable(LocaleId locale, TextTableId tableId, IReadOnlyDictionary<string, string> entries)
        {
            Locale = locale;
            TableId = tableId;
            _entries = entries ?? throw new ArgumentNullException(nameof(entries));
        }

        public bool TryGetTemplate(TextKey key, out string template)
        {
            if (key.Value == null)
            {
                template = null;
                return false;
            }

            return _entries.TryGetValue(key.Value, out template);
        }
    }
}