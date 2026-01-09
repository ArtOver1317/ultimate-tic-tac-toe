using System;
using R3;

namespace Runtime.UI.Core
{
    public abstract class BaseViewModel : IDisposable
    {
        private readonly CompositeDisposable _disposables = new();

        private readonly Subject<Unit> _closeRequested = new();
        private bool _isDisposed;

        public Observable<Unit> OnCloseRequested => _closeRequested;
        protected bool IsDisposed => _isDisposed;

        protected void RequestClose() => _closeRequested.OnNext(Unit.Default);

        protected void AddDisposable(IDisposable disposable) => _disposables.Add(disposable);

        public virtual void Initialize() { }

        public virtual void Reset()
        {
                if (_isDisposed)
                    return;
            
            // Signal to subscribers (coordinators) that this VM session is ending
            _closeRequested.OnNext(Unit.Default);
            _disposables.Clear();
            OnReset();
        }

        protected virtual void OnReset() { }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

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
