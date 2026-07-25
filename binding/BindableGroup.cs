using System;
using System.Collections;
using System.Collections.Generic;
using cfEngine;
using cfEngine.Rx;

namespace cfGodotEngine.Binding
{
    /// <summary>
    /// Godot-aware bindable group: a read-only, derived view that groups a
    /// source list by key. Constructed from a <see cref="BindableList{T}"/>
    /// (or any RxReadOnlyList) plus a key function; owns and disposes the
    /// inner <see cref="RxGroup{TKey,TValue}"/>. Binding nodes consume it
    /// through the IBindableMap channel; each item is a group
    /// (RxReadOnlyList of values) — enabling nested row binding.
    /// </summary>
    public class BindableGroup<TKey, TValue> : IBindableMap, IDisposable, IBindableMapUntyped
    {
        private readonly RxGroup<TKey, TValue> _group;
        private Relay<MapChange> _changeRelay;

        public BindableGroup(RxReadOnlyList<TValue> source, Func<TValue, TKey> keyFn)
        {
            _group = new RxGroup<TKey, TValue>(source, keyFn);
            _group.Events.Subscribe(OnGroupAdded, OnGroupRemoved, null);
        }

        public BindableGroup(BindableList<TValue> source, Func<TValue, TKey> keyFn)
            : this(source.Source, keyFn)
        {
        }

        /// <summary>Fully-typed granular events for game code.</summary>
        public ICollectionEvents<KeyValuePair<TKey, RxReadOnlyList<TValue>>> CollectionEvents => _group.Events;

        public int Count => _group.Count;

        public Type ItemType => typeof(RxReadOnlyList<TValue>);

        public Type KeyType => typeof(TKey);

        public RxReadOnlyList<TValue> this[TKey key] => _group[key];

        public IEnumerable<TKey> Keys => _group.Keys;

        public bool ContainsKey(TKey key) => _group.ContainsKey(key);

        public bool TryGetValue(TKey key, out RxReadOnlyList<TValue> value) => _group.TryGetValue(key, out value);

        public Res<TV, Exception> TryGet<TK, TV>(TK key)
        {
            if (key is not TKey typedKey)
                return Res.Err<TV>(new InvalidCastException($"Cannot cast key of type {typeof(TK).Name} to {typeof(TKey).Name}"));
            if (!_group.TryGetValue(typedKey, out var value))
                return Res.Err<TV>(new KeyNotFoundException($"Key not found: {typedKey}"));
            if (value is TV typed)
                return Res.Ok(typed);
            return Res.Err<TV>(new InvalidCastException($"Cannot cast group of type {value.GetType().Name} to {typeof(TV).Name}"));
        }

        public IEnumerable<TItem> Enumerate<TItem>()
        {
            if (!typeof(TItem).IsAssignableFrom(typeof(RxReadOnlyList<TValue>)))
            {
                Log.LogException(new InvalidCastException(
                    $"Cannot enumerate groups as {typeof(TItem).Name}"));
                yield break;
            }

            foreach (var kvp in _group)
                yield return (TItem)(object)kvp.Value;
        }

        public IEnumerable<TK> EnumerateKeys<TK>()
        {
            if (!typeof(TK).IsAssignableFrom(typeof(TKey)))
            {
                Log.LogException(new InvalidCastException(
                    $"Cannot enumerate {typeof(TKey).Name} keys as {typeof(TK).Name}"));
                yield break;
            }

            foreach (var key in _group.Keys)
                yield return (TK)(object)key;
        }

        /// <summary>Coarse any-change notification (IBindableBase), used by PropertyMap.</summary>
        public Subscription SubscribeChanged(Action handler)
        {
            EnsureRelay();
            return _changeRelay.AddListener(_ => handler());
        }

        /// <summary>Granular key-only change notification (IBindableMap).</summary>
        public Subscription SubscribeChanged(Action<MapChange> handler)
        {
            EnsureRelay();
            return _changeRelay.AddListener(handler);
        }

        public void Dispose() => _group.Dispose();

        private void EnsureRelay() => _changeRelay ??= new Relay<MapChange>(this);

        private void Dispatch(MapChange change) => _changeRelay?.Dispatch(change);

        private void OnGroupAdded(KeyValuePair<TKey, RxReadOnlyList<TValue>> kvp)
            => Dispatch(new MapChange(CollectionChangeKind.Add, kvp.Key));

        private void OnGroupRemoved(KeyValuePair<TKey, RxReadOnlyList<TValue>> kvp)
            => Dispatch(new MapChange(CollectionChangeKind.Remove, kvp.Key));

        IEnumerator IBindableCollectionUntyped.GetUntypedEnumerator() => _group.Values.GetEnumerator();

        IEnumerable IBindableMapUntyped.GetUntypedKeys() => _group.Keys;

        bool IBindableMapUntyped.TryGetUntyped(object key, out object value)
        {
            if (key is TKey typedKey && _group.TryGetValue(typedKey, out var v))
            {
                value = v;
                return true;
            }
            value = null;
            return false;
        }
    }
}
