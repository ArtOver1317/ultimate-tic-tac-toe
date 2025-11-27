using System;
using System.Collections.Generic;
using UnityEngine;

namespace Services.UI
{
    public class ObjectPool<T> where T : class
    {
        private readonly Dictionary<Type, Stack<T>> _pools = new();

        public TItem Get<TItem>(Type type) where TItem : class, T
        {
            if (_pools.TryGetValue(type, out var pool) && pool.Count > 0)
            {
                var item = (TItem)pool.Pop();
                Debug.Log($"[ObjectPool<{typeof(T).Name}>] Retrieved from pool: {type.Name} (remaining: {pool.Count})");
                return item;
            }

            return null;
        }

        public bool Return(Type type, T item, Action<T> onReturn = null)
        {
            if (!_pools.ContainsKey(type)) 
                _pools[type] = new Stack<T>();

            var pool = _pools[type];
            onReturn?.Invoke(item);
            pool.Push(item);
            Debug.Log($"[ObjectPool<{typeof(T).Name}>] Returned to pool: {type.Name} (pool size: {pool.Count})");
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

        public int GetSize(Type type) => _pools.TryGetValue(type, out var pool) ? pool.Count : 0;

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

