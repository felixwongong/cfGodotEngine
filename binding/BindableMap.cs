using System;
using System.Collections;
using System.Collections.Generic;
using cfEngine.Rx;
using Godot;

namespace cfGodotEngine.Binding
{
    /// <summary>
    /// Godot-aware bindable map over a stable <see cref="RxDictionary{TKey,TValue}"/>
    /// instance. Entries travel via <see cref="GetObject"/>, never as Variant;
    /// <see cref="GetVariant"/> returns the entry count.
    /// </summary>
    public class BindableMap<TKey, TValue> : IBindable, IReadOnlyDictionary<TKey, TValue>, IDisposable
    {
        private readonly RxDictionary<TKey, TValue> _dictionary = new();

        /// <summary>Read-only face of the inner dictionary.</summary>
        public RxReadOnlyDictionary<TKey, TValue> Source => _dictionary;

        public ICollectionEvents<KeyValuePair<TKey, TValue>> CollectionEvents => _dictionary.Events;

        public int Count => _dictionary.Count;

        public TValue this[TKey key] => _dictionary[key];

        public IEnumerable<TKey> Keys => _dictionary.Keys;

        public IEnumerable<TValue> Values => _dictionary.Values;

        public void Add(TKey key, TValue value) => _dictionary.Add(key, value);

        /// <summary>Inserts or updates the value for key, dispatching the appropriate event.</summary>
        public void Set(TKey key, TValue value) => _dictionary.Upsert(key, value);

        public bool Remove(TKey key) => _dictionary.Remove(key);

        public void Clear() => _dictionary.Clear();

        public void SetAll(IEnumerable<KeyValuePair<TKey, TValue>> items)
        {
            _dictionary.Clear();
            foreach (var (key, value) in items)
                _dictionary.Add(key, value);
        }

        public bool ContainsKey(TKey key) => _dictionary.ContainsKey(key);

        public bool TryGetValue(TKey key, out TValue value) => _dictionary.TryGetValue(key, out value);

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => _dictionary.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public Variant GetVariant() => _dictionary.Count;

        public object GetObject() => _dictionary;

        public Subscription SubscribeChanged(Action handler) => _dictionary.Events.OnChange(handler);

        public void Dispose() => _dictionary.Dispose();
    }
}
