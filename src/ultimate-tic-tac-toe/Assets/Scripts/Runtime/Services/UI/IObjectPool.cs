using System;
using System.Collections.Generic;

namespace Runtime.Services.UI
{
    public interface IObjectPool<T> where T : class
    {
        TItem Get<TItem>(Type type) where TItem : class, T;
        bool Return(Type type, T item, Action<T> onReturn = null);
        void Clear(Type type, Action<T> onClear = null);
        void ClearAll(Action<T> onClear = null);
        int GetSize(Type type);
        Dictionary<Type, int> GetStats();
    }
}

