using System;
using System.Collections.Generic;
using System.Threading;
using R3;

namespace Runtime.Localization
{
    /// <summary>
    /// Reactive Observe overloads: subscribe to localized text that auto-updates on locale/table changes.
    /// </summary>
    public sealed partial class LocalizationService
    {
        public Observable<string> Observe(TextTableId table, TextKey key, Observable<IReadOnlyDictionary<string, object>> args)
        {
            if (args == null)
                throw new ArgumentNullException(nameof(args));

            EnsureInitialized();

            TrackTable(table);

            return Observable.Create<string>(observer =>
            {
                IReadOnlyDictionary<string, object> latestArgs = null;

                string lastEmitted = null;
                var isDisposed = 0;

                void Emit()
                {
                    if (Volatile.Read(ref isDisposed) != 0)
                        return;

                    var text = Resolve(table, key, latestArgs);
                    
                    if (string.Equals(text, lastEmitted, StringComparison.Ordinal))
                        return;

                    lastEmitted = text;
                    observer.OnNext(text);
                }

                var argsSub = args.Subscribe(a =>
                {
                    latestArgs = a;
                    Emit();
                });

                var localeSub = CurrentLocale.Subscribe(_ => Emit());

                var storeSub = SubscribeToStoreEvents(table, Emit);

                Emit();

                return Disposable.Create(() =>
                {
                    Interlocked.Exchange(ref isDisposed, 1);
                    argsSub.Dispose();
                    localeSub.Dispose();
                    storeSub.Dispose();
                });
            });
        }

        public Observable<string> Observe(TextTableId table, TextKey key, IReadOnlyDictionary<string, object> args = null)
        {
            EnsureInitialized();

            TrackTable(table);

            return Observable.Create<string>(observer =>
            {
                observer.OnNext(Resolve(table, key, args));

                var lastLocale = CurrentLocale.CurrentValue;
                
                var localeSub = CurrentLocale.Subscribe(newLocale =>
                {
                    if (newLocale == lastLocale)
                        return;

                    lastLocale = newLocale;
                    observer.OnNext(Resolve(table, key, args));
                });

                var storeSub = SubscribeToStoreEvents(table, () => observer.OnNext(Resolve(table, key, args)));

                return Disposable.Create(() =>
                {
                    localeSub.Dispose();
                    storeSub.Dispose();
                });
            });
        }

        /// <summary>
        /// Subscribes to store load/unload events relevant to the given table.
        /// Calls <paramref name="onRelevantChange"/> when the table is loaded/unloaded for the active locale's fallback chain.
        /// </summary>
        private IDisposable SubscribeToStoreEvents(TextTableId table, Action onRelevantChange)
        {
            var storeEvents = _store.Events;

            if (storeEvents == null)
                return Disposable.Empty;

            return storeEvents.Subscribe(e =>
            {
                if (e.Type != LocalizationStoreEventType.TableLoaded && e.Type != LocalizationStoreEventType.TableUnloaded)
                    return;

                if (e.TableId != table)
                    return;

                var activeLocale = _store.GetActiveLocale();
                var chain = _policy.GetFallbackChain(activeLocale);

                for (var i = 0; i < chain.Count; i++)
                {
                    if (chain[i] == e.Locale)
                    {
                        onRelevantChange();
                        return;
                    }
                }
            });
        }
    }
}
