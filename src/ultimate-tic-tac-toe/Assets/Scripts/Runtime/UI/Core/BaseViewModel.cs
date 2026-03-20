using System;
using R3;

namespace Runtime.UI.Core
{
    public abstract class BaseViewModel : IDisposable
    {
        private readonly CompositeDisposable _disposables = new();

        private readonly Subject<Unit> _closeRequested = new();

        public Observable<Unit> OnCloseRequested => _closeRequested;
        protected bool IsDisposed { get; private set; }

        protected void RequestClose() => _closeRequested.OnNext(Unit.Default);

        protected void AddDisposable(IDisposable disposable) => _disposables.Add(disposable);

        public virtual void Initialize() { }

        public virtual void Reset()
        {
            if (IsDisposed)
                return;
            
            // Signal to subscribers (coordinators) that this VM session is ending
            _closeRequested.OnNext(Unit.Default);
            _disposables.Clear();
            OnReset();
        }

        protected virtual void OnReset() { }

        public void Dispose()
        {
            if (IsDisposed)
                return;
            
            IsDisposed = true;

            OnDispose();
            // Ensure subscribers know we are closing
            _closeRequested.OnNext(Unit.Default);
            _closeRequested.OnCompleted();
            _closeRequested.Dispose();
            _disposables.Dispose();
        }

        protected virtual void OnDispose() { }
    }
}