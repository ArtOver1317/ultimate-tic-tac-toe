using System;
using System.Collections.Generic;
using UnityEngine;

namespace Runtime.Services.UI
{
    public class ObjectPool<T> : IObjectPool<T> where T : class
    {
        private readonly Dictionary<Type, Stack<T>> _pools = new();

        private static Type GetItemType<TItem>() where TItem : class, T => typeof(TItem);

        public TItem Get<TItem>() where TItem : class, T
        {
            var itemType = GetItemType<TItem>();
            
            if (_pools.TryGetValue(itemType, out var pool) && pool.Count > 0)
            {
                var item = (TItem)pool.Pop();
                Debug.Log($"[ObjectPool<{typeof(T).Name}>] Retrieved from pool: {itemType.Name} (remaining: {pool.Count})");
                return item;
            }

            return null;
        }

        public TItem Get<TItem>(Type requestedType) where TItem : class, T
        {
            var itemType = GetItemType<TItem>();
            
            if (requestedType != null && requestedType != itemType)
                Debug.LogWarning($"[ObjectPool<{typeof(T).Name}>] Requested {requestedType.Name}, but pool stores {itemType.Name}. Using {itemType.Name} stack.");
            
            return Get<TItem>();
        }

        public bool Return<TItem>(TItem item, Action<T> onReturn = null) where TItem : class, T
        {
            var itemType = GetItemType<TItem>();
            
            if (!_pools.TryGetValue(itemType, out var pool))
                pool = _pools[itemType] = new Stack<T>();
            
            onReturn?.Invoke(item);
            pool.Push(item);
            Debug.Log($"[ObjectPool<{typeof(T).Name}>] Returned to pool: {itemType.Name} (pool size: {pool.Count})");
            return true;
        }

        public bool Return(Type type, T item, Action<T> onReturn = null)
        {
            var resolvedType = type ?? item?.GetType();
            
            if (resolvedType == null)
                throw new ArgumentNullException(nameof(type), "Cannot return object without a type.");
            
            if (!_pools.TryGetValue(resolvedType, out var pool))
                pool = _pools[resolvedType] = new Stack<T>();
            
            onReturn?.Invoke(item);
            pool.Push(item);
            Debug.Log($"[ObjectPool<{typeof(T).Name}>] Returned to pool: {resolvedType.Name} (pool size: {pool.Count})");
            return true;
        }

        public void Clear(Type type, Action<T> onClear = null)
        {
            if (_pools.TryGetValue(type, out var pool))
            {
                while (pool.Count > 0)
                {
                    var item = pool.Pop();
                    onClear?.Invoke(item);
                }
                
                _pools.Remove(type);
                Debug.Log($"[ObjectPool<{typeof(T).Name}>] Cleared pool for {type.Name}");
            }
        }

        public void ClearAll(Action<T> onClear = null)
        {
            foreach (var pool in _pools.Values)
            {
                while (pool.Count > 0)
                {
                    var item = pool.Pop();
                    onClear?.Invoke(item);
                }
            }
            
            _pools.Clear();
            Debug.Log($"[ObjectPool<{typeof(T).Name}>] All pools cleared");
        }

        public int GetSize(Type type)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));
            
            return _pools.TryGetValue(type, out var pool) ? pool.Count : 0;
        }

        public Dictionary<Type, int> GetStats()
        {
            var stats = new Dictionary<Type, int>();
            
            foreach (var kvp in _pools)
            {
                stats[kvp.Key] = kvp.Value.Count;
            }

            return stats;
        }
    }
}