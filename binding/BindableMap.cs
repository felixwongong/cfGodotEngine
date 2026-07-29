using System;
using System.Collections;
using System.Collections.Generic;
using cfEngine;
using cfEngine.Rx;

namespace cfGodotEngine.Binding
{
    /// <summary>
    /// Godot-aware bindable map over a stable <see cref="RxDictionary{TKey,TValue}"/>
    /// instance. Game code uses the fully-typed API; binding nodes use the
    /// IBindableMap channel (typed TryGet/EnumerateKeys, key-only change events).
    /// SetAll coalesces to a single Reset on the binding channel.
    /// Note: unordered — UI needing stable order should use BindableList.
    /// </summary>
    public class BindableMap<TKey, TValue> : IBindableMap, IReadOnlyDictionary<TKey, TValue>, IDisposable, IBindableMapUntyped
    {
        private readonly RxDictionary<TKey, TValue> _dictionary = new();
        private readonly Subscription _selfSub;
        private Relay<MapChange> _changeRelay;
        private bool _suppressItemEvents;
        private Subscription _sourceSub;
        private RxReadOnlyDictionary<TKey, TValue> _ownedSource;

        public BindableMap()
        {
            // The handle MUST be retained: relay listeners are weak-referenced,
            // so a discarded Subscription is GC-collected and updates silently stop.
            _selfSub = _dictionary.Events.Subscribe(OnEntryAdded, OnEntryRemoved, OnEntryUpdated);
        }

        /// <summary>Read-only face of the inner dictionary.</summary>
        public RxReadOnlyDictionary<TKey, TValue> Source => _dictionary;

        /// <summary>Fully-typed granular events for game code.</summary>
        public ICollectionEvents<KeyValuePair<TKey, TValue>> CollectionEvents => _dictionary.Events;

        public int Count => _dictionary.Count;

        public Type ItemType => typeof(TValue);

        public Type KeyType => typeof(TKey);

        public TValue this[TKey key] => _dictionary[key];

        public IEnumerable<TKey> Keys => _dictionary.Keys;

        public IEnumerable<TValue> Values => _dictionary.Values;

        public void Add(TKey key, TValue value)
        {
            DetachSource();
            _dictionary.Add(key, value);
        }

        /// <summary>Inserts or updates the value for key, dispatching the appropriate event.</summary>
        public void Set(TKey key, TValue value)
        {
            DetachSource();
            _dictionary.Upsert(key, value);
        }

        public bool Remove(TKey key)
        {
            DetachSource();
            return _dictionary.Remove(key);
        }

        public void Clear()
        {
            DetachSource();
            _dictionary.Clear();
        }

        /// <summary>
        /// Replaces all entries. Game-code subscribers see the individual
        /// removes/adds via CollectionEvents; binding subscribers see one Reset.
        /// </summary>
        public void SetAll(IEnumerable<KeyValuePair<TKey, TValue>> items)
        {
            DetachSource();
            SetAllLocal(items);
        }

        /// <summary>
        /// Mirrors a source dictionary: applies its current entries and forwards
        /// granular add/remove/update changes. Last-write-wins: any local mutation
        /// detaches the source. The bindable takes ownership of
        /// <paramref name="source"/> and disposes it on detach/<see cref="Dispose"/>
        /// (cleans up projection chains).
        /// </summary>
        public void SetSource(RxReadOnlyDictionary<TKey, TValue> source)
        {
            if (source == null)
            {
                ClearSource();
                return;
            }

            DetachSource(warn: false);
            _ownedSource = source;
            SetAllLocal(source);
            _sourceSub = source.Events.Subscribe(
                kvp => _dictionary.Add(kvp.Key, kvp.Value),
                kvp => _dictionary.Remove(kvp.Key),
                (_, newKvp) => _dictionary.Upsert(newKvp.Key, newKvp.Value));
        }

        /// <summary>Detaches the current source without changing the entries.</summary>
        public void ClearSource() => DetachSource(warn: false);

        private void DetachSource(bool warn = true)
        {
            if (_sourceSub == null && _ownedSource == null)
                return;
            if (warn)
                Log.LogWarning($"[BindableMap<{typeof(TKey).Name},{typeof(TValue).Name}>] Local mutation detached the Rx source");
            _sourceSub?.Unsubscribe();
            _sourceSub = null;
            _ownedSource?.Dispose();
            _ownedSource = null;
        }

        private void SetAllLocal(IEnumerable<KeyValuePair<TKey, TValue>> items)
        {
            _suppressItemEvents = true;
            _dictionary.Clear();
            foreach (var (key, value) in items)
                _dictionary.Add(key, value);
            _suppressItemEvents = false;
            Dispatch(new MapChange(CollectionChangeKind.Reset, null));
        }

        public Res<TV, Exception> TryGet<TK, TV>(TK key)
        {
            if (key is not TKey typedKey)
                return Res.Err<TV>(new InvalidCastException($"Cannot cast key of type {typeof(TK).Name} to {typeof(TKey).Name}"));
            if (!_dictionary.TryGetValue(typedKey, out var value))
                return Res.Err<TV>(new KeyNotFoundException($"Key not found: {typedKey}"));
            if (value is TV typed)
                return Res.Ok(typed);
            return Res.Err<TV>(new InvalidCastException($"Cannot cast value of type {typeof(TValue).Name} to {typeof(TV).Name}"));
        }

        public IEnumerable<TItem> Enumerate<TItem>()
        {
            if (!typeof(TItem).IsAssignableFrom(typeof(TValue)))
            {
                Log.LogException(new InvalidCastException(
                    $"Cannot enumerate {typeof(TValue).Name} values as {typeof(TItem).Name}"));
                yield break;
            }

            foreach (var value in _dictionary.Values)
                yield return (TItem)(object)value;
        }

        public IEnumerable<TK> EnumerateKeys<TK>()
        {
            if (!typeof(TK).IsAssignableFrom(typeof(TKey)))
            {
                Log.LogException(new InvalidCastException(
                    $"Cannot enumerate {typeof(TKey).Name} keys as {typeof(TK).Name}"));
                yield break;
            }

            foreach (var key in _dictionary.Keys)
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

        public bool ContainsKey(TKey key) => _dictionary.ContainsKey(key);

        public bool TryGetValue(TKey key, out TValue value) => _dictionary.TryGetValue(key, out value);

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => _dictionary.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public void Dispose()
        {
            DetachSource(warn: false);
            _selfSub.UnsubscribeIfNotNull();
            _dictionary.Dispose();
        }

        private void EnsureRelay() => _changeRelay ??= new Relay<MapChange>(this);

        private void Dispatch(MapChange change) => _changeRelay?.Dispatch(change);

        private void OnEntryAdded(KeyValuePair<TKey, TValue> kvp)
        {
            if (!_suppressItemEvents)
                Dispatch(new MapChange(CollectionChangeKind.Add, kvp.Key));
        }

        private void OnEntryRemoved(KeyValuePair<TKey, TValue> kvp)
        {
            if (!_suppressItemEvents)
                Dispatch(new MapChange(CollectionChangeKind.Remove, kvp.Key));
        }

        private void OnEntryUpdated(KeyValuePair<TKey, TValue> oldEntry, KeyValuePair<TKey, TValue> newEntry)
        {
            if (!_suppressItemEvents)
                Dispatch(new MapChange(CollectionChangeKind.Update, newEntry.Key));
        }

        IEnumerator IBindableCollectionUntyped.GetUntypedEnumerator() => _dictionary.Values.GetEnumerator();

        IEnumerable IBindableMapUntyped.GetUntypedKeys() => _dictionary.Keys;

        bool IBindableMapUntyped.TryGetUntyped(object key, out object value)
        {
            if (key is TKey typedKey && _dictionary.TryGetValue(typedKey, out var v))
            {
                value = v;
                return true;
            }
            value = null;
            return false;
        }
    }
}
