using System;
using System.Collections.Generic;
using cfEngine.Rx;
using Godot;

namespace cfGodotEngine.Binding
{
    /// <summary>
    /// Godot-aware bindable group: a read-only, derived view that groups a
    /// source list by key. Constructed from a <see cref="BindableList{T}"/>
    /// (or any RxReadOnlyList) plus a key function; owns and disposes the
    /// inner <see cref="RxGroup{TKey,TValue}"/>.
    /// <see cref="GetVariant"/> returns the group count.
    /// </summary>
    public class BindableGroup<TKey, TValue> : IBindable, IDisposable
    {
        private readonly RxGroup<TKey, TValue> _group;

        public BindableGroup(RxReadOnlyList<TValue> source, Func<TValue, TKey> keyFn)
        {
            _group = new RxGroup<TKey, TValue>(source, keyFn);
        }

        public BindableGroup(BindableList<TValue> source, Func<TValue, TKey> keyFn)
            : this(source.Source, keyFn)
        {
        }

        public ICollectionEvents<KeyValuePair<TKey, RxReadOnlyList<TValue>>> CollectionEvents => _group.Events;

        public int Count => _group.Count;

        public RxReadOnlyList<TValue> this[TKey key] => _group[key];

        public IEnumerable<TKey> Keys => _group.Keys;

        public bool ContainsKey(TKey key) => _group.ContainsKey(key);

        public bool TryGetValue(TKey key, out RxReadOnlyList<TValue> value) => _group.TryGetValue(key, out value);

        public Variant GetVariant() => _group.Count;

        public object GetObject() => _group;

        public Subscription SubscribeChanged(Action handler) => _group.Events.OnChange(handler);

        public void Dispose() => _group.Dispose();
    }
}
