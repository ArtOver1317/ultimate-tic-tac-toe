using System;
using System.Collections.Generic;
using System.Threading;
using R3;

namespace Runtime.GameModes.Wizard
{
    public static class GameModeWizardObservableExtensions
    {
        /// <summary>
        /// Filters a snapshot stream so that only changes with a different Version are emitted.
        /// Useful in Unity projects where snapshot DTOs intentionally do not implement value-based equality.
        /// </summary>
        public static Observable<GameModeSessionSnapshot> DistinctUntilVersionChanged(this Observable<GameModeSessionSnapshot> source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            return Observable.Create<GameModeSessionSnapshot>(observer =>
            {
                var hasLast = false;
                var lastVersion = 0;

                var sub = source.Subscribe(new ForwardObserver<GameModeSessionSnapshot, GameModeSessionSnapshot>(
                    observer,
                    onNext: snapshot =>
                    {
                        if (snapshot == null)
                            return;

                        if (!hasLast || snapshot.Version != lastVersion)
                        {
                            hasLast = true;
                            lastVersion = snapshot.Version;
                            observer.OnNext(snapshot);
                        }
                    }));

                return Disposable.Create(sub.Dispose);
            });
        }

        /// <summary>
        /// Projects a snapshot stream and emits only when projected value changes.
        /// </summary>
        public static Observable<T> SelectDistinct<T>(
            this Observable<GameModeSessionSnapshot> source,
            Func<GameModeSessionSnapshot, T> selector,
            IEqualityComparer<T> comparer = null)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (selector == null)
                throw new ArgumentNullException(nameof(selector));

            comparer ??= EqualityComparer<T>.Default;

            return Observable.Create<T>(observer =>
            {
                var hasLast = false;
                T last = default;

                var sub = source.Subscribe(new ForwardObserver<GameModeSessionSnapshot, T>(
                    observer,
                    onNext: snapshot =>
                    {
                        if (snapshot == null)
                            return;

                        var current = selector(snapshot);

                        if (hasLast && comparer.Equals(last, current))
                            return;

                        hasLast = true;
                        last = current;
                        observer.OnNext(current);
                    }));

                return Disposable.Create(sub.Dispose);
            });
        }

        private sealed class ForwardObserver<TIn, TOut> : Observer<TIn>
        {
            private readonly Observer<TOut> _downstream;
            private readonly Action<TIn> _onNext;
            private int _isTerminated;

            public ForwardObserver(Observer<TOut> downstream, Action<TIn> onNext)
            {
                _downstream = downstream ?? throw new ArgumentNullException(nameof(downstream));
                _onNext = onNext ?? throw new ArgumentNullException(nameof(onNext));
            }

            protected override void OnNextCore(TIn value)
            {
                if (Volatile.Read(ref _isTerminated) != 0)
                    return;

                _onNext(value);
            }

            protected override void OnErrorResumeCore(Exception error)
            {
                if (Interlocked.Exchange(ref _isTerminated, 1) != 0)
                    return;

                _downstream.OnErrorResume(error);
            }

            protected override void OnCompletedCore(Result result)
            {
                if (Interlocked.Exchange(ref _isTerminated, 1) != 0)
                    return;

                _downstream.OnCompleted(result);
            }
        }
    }
}
