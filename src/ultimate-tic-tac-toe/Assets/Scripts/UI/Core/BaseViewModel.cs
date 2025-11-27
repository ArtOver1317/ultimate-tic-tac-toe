using System;
using R3;

namespace UI.Core
{
    public abstract class BaseViewModel : IDisposable
    {
        private readonly CompositeDisposable _disposables = new();

        public Subject<Unit> OnCloseRequested { get; } = new();

        protected void AddDisposable(IDisposable disposable) => _disposables.Add(disposable);

        public virtual void Initialize() { }

        public virtual void Reset()
        {
            _disposables.Clear();
            OnReset();
        }

        protected virtual void OnReset() { }

        public void Dispose()
        {
            OnDispose();
            OnCloseRequested?.Dispose();
            _disposables.Dispose();
        }

        protected virtual void OnDispose() { }

        public void RequestClose() => OnCloseRequested.OnNext(Unit.Default);
    }
}