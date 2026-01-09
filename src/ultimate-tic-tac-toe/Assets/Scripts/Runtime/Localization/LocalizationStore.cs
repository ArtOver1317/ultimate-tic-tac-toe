using System;
using System.Collections.Generic;
using R3;

namespace Runtime.Localization
{
    public sealed class LocalizationStore : ILocalizationStore, IDisposable
    {
        private readonly ILocalizationPolicy _policy;
        private readonly Subject<LocalizationStoreEvent> _events = new();

        private LocaleId _activeLocale;

        private readonly Dictionary<CacheKey, LocalizationTable> _tables = new();
        private readonly LinkedList<CacheKey> _lru = new();
        private readonly Dictionary<CacheKey, LinkedListNode<CacheKey>> _lruNodes = new();

        public Observable<LocalizationStoreEvent> Events => _events;

        public LocalizationStore(ILocalizationPolicy policy)
        {
            _policy = policy ?? throw new ArgumentNullException(nameof(policy));
            _activeLocale = _policy.DefaultLocale;
        }

        public void SetActiveLocale(LocaleId locale)
        {
            _activeLocale = locale;
            _events.OnNext(new LocalizationStoreEvent(LocalizationStoreEventType.ActiveLocaleChanged, locale, default, string.Empty));
        }

        public LocaleId GetActiveLocale() => _activeLocale;

        public void Put(LocalizationTable table)
        {
            if (table == null)
                throw new ArgumentNullException(nameof(table));

            var key = new CacheKey(table.Locale, table.TableId);
            _tables[key] = table;
            Touch(key);

            _events.OnNext(new LocalizationStoreEvent(LocalizationStoreEventType.TableLoaded, table.Locale, table.TableId, string.Empty));

            EnforceCacheLimit();
        }

        public bool TryResolveTemplate(TextTableId table, TextKey key, out string template)
        {
            var chain = _policy.GetFallbackChain(_activeLocale);
            
            foreach (var locale in chain)
            {
                var cacheKey = new CacheKey(locale, table);

                if (_tables.TryGetValue(cacheKey, out var localizationTable))
                {
                    Touch(cacheKey);

                    if (localizationTable.TryGetTemplate(key, out template))
                        return true;
                }
            }

            template = null;
            return false;
        }

        public void Remove(LocaleId locale, TextTableId table)
        {
            var key = new CacheKey(locale, table);
            
            if (!_tables.Remove(key))
                return;

            if (_lruNodes.Remove(key, out var node)) 
                _lru.Remove(node);

            _events.OnNext(new LocalizationStoreEvent(LocalizationStoreEventType.TableUnloaded, locale, table, string.Empty));
        }

        public void Dispose() => _events.Dispose();

        private void EnforceCacheLimit()
        {
            var max = _policy.MaxCachedTables;
            
            while (_tables.Count > max)
            {
                var last = _lru.Last;
                
                if (last == null)
                    return;

                var key = last.Value;
                _lru.RemoveLast();
                _lruNodes.Remove(key);

                _tables.Remove(key);
                _events.OnNext(new LocalizationStoreEvent(LocalizationStoreEventType.TableUnloaded, key.Locale, key.Table, "Evicted"));
            }
        }

        private void Touch(CacheKey key)
        {
            if (_lruNodes.TryGetValue(key, out var node))
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
                return;
            }

            var newNode = _lru.AddFirst(key);
            _lruNodes[key] = newNode;
        }

        private readonly struct CacheKey : IEquatable<CacheKey>
        {
            public LocaleId Locale { get; }
            public TextTableId Table { get; }

            public CacheKey(LocaleId locale, TextTableId table)
            {
                Locale = locale;
                Table = table;
            }

            public bool Equals(CacheKey other) => Locale == other.Locale && Table == other.Table;
            public override bool Equals(object obj) => obj is CacheKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    return (Locale.GetHashCode() * 397) ^ Table.GetHashCode();
                }
            }
        }
    }
}
