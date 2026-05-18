using System;
using System.Collections;
using System.Collections.Generic;

#nullable disable

namespace Amanda
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class AutoDirtyAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class AutoDirtyPropertyAttribute : Attribute
    {
        public AutoDirtyPropertyAttribute(string name, Type type)
        {
        }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = false)]
    public sealed class AutoDirtyIgnoreAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Field, Inherited = false)]
    public sealed class AutoDirtyPropertyNameAttribute : Attribute
    {
        public AutoDirtyPropertyNameAttribute(string name)
        {
        }
    }

    public interface IAutoDirtyNode
    {
        void SetDirtyCallback(Action markDirty);
    }

    public sealed class AutoDirtyList<T> : IList<T>
    {
        private readonly IList<T> _inner;
        private readonly Action _markDirty;

        public AutoDirtyList(IList<T> inner, Action markDirty)
        {
            _inner = inner;
            _markDirty = markDirty;

            for (var i = 0; i < _inner.Count; i++)
            {
                BindChild(_inner[i]);
            }
        }

        public T this[int index]
        {
            get => _inner[index];
            set
            {
                if (EqualityComparer<T>.Default.Equals(_inner[index], value))
                    return;

                _inner[index] = value;
                BindChild(value);
                _markDirty();
            }
        }

        public int Count => _inner.Count;

        public bool IsReadOnly => _inner.IsReadOnly;

        public void Add(T item)
        {
            _inner.Add(item);
            BindChild(item);
            _markDirty();
        }

        public void Clear()
        {
            if (_inner.Count == 0)
                return;

            _inner.Clear();
            _markDirty();
        }

        public bool Contains(T item) => _inner.Contains(item);

        public void CopyTo(T[] array, int arrayIndex) => _inner.CopyTo(array, arrayIndex);

        public IEnumerator<T> GetEnumerator() => _inner.GetEnumerator();

        public int IndexOf(T item) => _inner.IndexOf(item);

        public void Insert(int index, T item)
        {
            _inner.Insert(index, item);
            BindChild(item);
            _markDirty();
        }

        public bool Remove(T item)
        {
            var removed = _inner.Remove(item);
            if (removed)
                _markDirty();

            return removed;
        }

        public void RemoveAt(int index)
        {
            _inner.RemoveAt(index);
            _markDirty();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private void BindChild(T item)
        {
            if (item is IAutoDirtyNode child)
                child.SetDirtyCallback(_markDirty);
        }
    }

    public sealed class AutoDirtyDictionary<TKey, TValue> : IDictionary<TKey, TValue>
    {
        private readonly IDictionary<TKey, TValue> _inner;
        private readonly Action _markDirty;

        public AutoDirtyDictionary(IDictionary<TKey, TValue> inner, Action markDirty)
        {
            _inner = inner;
            _markDirty = markDirty;

            foreach (var value in _inner.Values)
            {
                BindChild(value);
            }
        }

        public TValue this[TKey key]
        {
            get => _inner[key];
            set
            {
                if (_inner.TryGetValue(key, out var oldValue) &&
                    EqualityComparer<TValue>.Default.Equals(oldValue, value))
                    return;

                _inner[key] = value;
                BindChild(value);
                _markDirty();
            }
        }

        public ICollection<TKey> Keys => _inner.Keys;

        public ICollection<TValue> Values => _inner.Values;

        public int Count => _inner.Count;

        public bool IsReadOnly => _inner.IsReadOnly;

        public void Add(TKey key, TValue value)
        {
            _inner.Add(key, value);
            BindChild(value);
            _markDirty();
        }

        public void Add(KeyValuePair<TKey, TValue> item)
        {
            _inner.Add(item);
            BindChild(item.Value);
            _markDirty();
        }

        public void Clear()
        {
            if (_inner.Count == 0)
                return;

            _inner.Clear();
            _markDirty();
        }

        public bool Contains(KeyValuePair<TKey, TValue> item) => _inner.Contains(item);

        public bool ContainsKey(TKey key) => _inner.ContainsKey(key);

        public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex) => _inner.CopyTo(array, arrayIndex);

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => _inner.GetEnumerator();

        public bool Remove(TKey key)
        {
            var removed = _inner.Remove(key);
            if (removed)
                _markDirty();

            return removed;
        }

        public bool Remove(KeyValuePair<TKey, TValue> item)
        {
            var removed = _inner.Remove(item);
            if (removed)
                _markDirty();

            return removed;
        }

        public bool TryGetValue(TKey key, out TValue value) => _inner.TryGetValue(key, out value);

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private void BindChild(TValue value)
        {
            if (value is IAutoDirtyNode child)
                child.SetDirtyCallback(_markDirty);
        }
    }

    public sealed class AutoDirtySet<T> : ISet<T>
    {
        private readonly ISet<T> _inner;
        private readonly Action _markDirty;

        public AutoDirtySet(ISet<T> inner, Action markDirty)
        {
            _inner = inner;
            _markDirty = markDirty;

            foreach (var item in _inner)
            {
                BindChild(item);
            }
        }

        public T this[int index]
        {
            get
            {
                var i = 0;
                foreach (var item in _inner)
                {
                    if (i == index)
                        return item;

                    i++;
                }

                throw new ArgumentOutOfRangeException(nameof(index));
            }
            set
            {
                var oldValue = this[index];
                if (EqualityComparer<T>.Default.Equals(oldValue, value))
                    return;

                _inner.Remove(oldValue);
                _inner.Add(value);
                BindChild(value);
                _markDirty();
            }
        }

        public int Count => _inner.Count;

        public bool IsReadOnly => _inner.IsReadOnly;

        public bool Add(T item)
        {
            var added = _inner.Add(item);
            if (added)
            {
                BindChild(item);
                _markDirty();
            }

            return added;
        }

        void ICollection<T>.Add(T item)
        {
            Add(item);
        }

        public void Clear()
        {
            if (_inner.Count == 0)
                return;

            _inner.Clear();
            _markDirty();
        }

        public bool Contains(T item) => _inner.Contains(item);

        public void CopyTo(T[] array, int arrayIndex) => _inner.CopyTo(array, arrayIndex);

        public void ExceptWith(IEnumerable<T> other)
        {
            var before = _inner.Count;
            _inner.ExceptWith(other);
            if (_inner.Count != before)
                _markDirty();
        }

        public IEnumerator<T> GetEnumerator() => _inner.GetEnumerator();

        public void IntersectWith(IEnumerable<T> other)
        {
            var snapshot = new HashSet<T>(_inner);
            _inner.IntersectWith(other);
            if (!snapshot.SetEquals(_inner))
                _markDirty();
        }

        public bool IsProperSubsetOf(IEnumerable<T> other) => _inner.IsProperSubsetOf(other);

        public bool IsProperSupersetOf(IEnumerable<T> other) => _inner.IsProperSupersetOf(other);

        public bool IsSubsetOf(IEnumerable<T> other) => _inner.IsSubsetOf(other);

        public bool IsSupersetOf(IEnumerable<T> other) => _inner.IsSupersetOf(other);

        public bool Overlaps(IEnumerable<T> other) => _inner.Overlaps(other);

        public bool Remove(T item)
        {
            var removed = _inner.Remove(item);
            if (removed)
                _markDirty();

            return removed;
        }

        public bool SetEquals(IEnumerable<T> other) => _inner.SetEquals(other);

        public void SymmetricExceptWith(IEnumerable<T> other)
        {
            var snapshot = new HashSet<T>(_inner);
            _inner.SymmetricExceptWith(other);
            if (!snapshot.SetEquals(_inner))
                _markDirty();
        }

        public void UnionWith(IEnumerable<T> other)
        {
            var changed = false;
            foreach (var item in other)
            {
                if (_inner.Add(item))
                {
                    BindChild(item);
                    changed = true;
                }
            }

            if (changed)
                _markDirty();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private void BindChild(T item)
        {
            if (item is IAutoDirtyNode child)
                child.SetDirtyCallback(_markDirty);
        }
    }
}
