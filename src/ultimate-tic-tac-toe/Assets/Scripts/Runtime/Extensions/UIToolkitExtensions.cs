using System;
using R3;
using UnityEngine.UIElements;

namespace Runtime.Extensions
{
    public static class UIToolkitExtensions
    {
        public static Observable<Unit> OnClickAsObservable(this Button button) =>
            Observable.Create<Unit>(observer =>
            {
                Action handler = () => observer.OnNext(Unit.Default);
                button.clicked += handler;
                return Disposable.Create(() => button.clicked -= handler);
            });
    }
}