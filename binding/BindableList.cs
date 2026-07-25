using System;
using System.Collections;
using System.Collections.Generic;
using cfEngine;
using cfEngine.Rx;

namespace cfGodotEngine.Binding
{
    /// <summary>
    /// Godot-aware bindable list over a stable <see cref="RxList{T}"/> instance.
    /// The inner list identity never changes. Game code uses the fully-typed
    /// API (indexer, CollectionEvents); binding nodes use the IBindableList
    /// channel (typed TryGet/Enumerate, index-only change events).
    /// SetAll coalesces to a single Reset on the binding channel.
    /// </summary>
    public class BindableList<T> : IBindableList, IReadOnlyList<T>, IDisposable, IBindableListUntyped
    {
        private readonly RxList<T> _list;
        private Relay<ListChange> _changeRelay;
        private bool _suppressItemEvents;

        public BindableList()
        {
            _list = new RxList<T>();
            _list.Events.Subscribe(OnItemAdded, OnItemRemoved, OnItemUpdated);
        }

        public BindableList(IEnumerable<T> items) : this()
        {
            foreach (var item in items)
                _list.Add(item);
        }

        /// <summary>Read-only face of the inner list (e.g. to feed a BindableGroup).</summary>
        public RxReadOnlyList<T> Source => _list;

        /// <summary>Fully-typed granular events for game code.</summary>
        public ICollectionEvents<(int index, T item)> CollectionEvents => _list.Events;

        public int Count => _list.Count;

        public Type ItemType => typeof(T);

        public T this[int index] => _list[index];

        public void Add(T item) => _list.Add(item);

        public void Insert(int index, T item) => _list.Insert(index, item);

        public bool Remove(T item) => _list.Remove(item);

        public void RemoveAt(int index) => _list.RemoveAt(index);

        public void Update(int index, T item) => _list.Update(index, item);

        public void Clear() => _list.Clear();

        /// <summary>
        /// Replaces all items. Game-code subscribers see the individual
        /// removes/adds via CollectionEvents; binding subscribers see one Reset.
        /// </summary>
        public void SetAll(IEnumerable<T> items)
        {
            _suppressItemEvents = true;
            _list.Clear();
            foreach (var item in items)
                _list.Add(item);
            _suppressItemEvents = false;
            Dispatch(new ListChange(CollectionChangeKind.Reset, -1));
        }

        public Res<TItem, Exception> TryGet<TItem>(int index)
        {
            if ((uint)index >= (uint)_list.Count)
                return Res.Err<TItem>(new IndexOutOfRangeException($"Index {index} out of range (count {_list.Count})"));
            if (_list[index] is TItem typed)
                return Res.Ok(typed);
            return Res.Err<TItem>(new InvalidCastException($"Cannot cast item of type {typeof(T).Name} to {typeof(TItem).Name}"));
        }

        public IEnumerable<TItem> Enumerate<TItem>()
        {
            if (!typeof(TItem).IsAssignableFrom(typeof(T)))
            {
                Log.LogException(new InvalidCastException(
                    $"Cannot enumerate {typeof(T).Name} items as {typeof(TItem).Name}"));
                yield break;
            }

            foreach (var item in _list)
                yield return (TItem)(object)item;
        }

        /// <summary>Coarse any-change notification (IBindableBase), used by PropertyMap.</summary>
        public Subscription SubscribeChanged(Action handler)
        {
            EnsureRelay();
            return _changeRelay.AddListener(_ => handler());
        }

        /// <summary>Granular index-only change notification (IBindableList).</summary>
        public Subscription SubscribeChanged(Action<ListChange> handler)
        {
            EnsureRelay();
            return _changeRelay.AddListener(handler);
        }

        public IEnumerator<T> GetEnumerator() => _list.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public void Dispose() => _list.Dispose();

        private void EnsureRelay() => _changeRelay ??= new Relay<ListChange>(this);

        private void Dispatch(ListChange change) => _changeRelay?.Dispatch(change);

        private void OnItemAdded((int index, T item) e)
        {
            if (!_suppressItemEvents)
                Dispatch(new ListChange(CollectionChangeKind.Add, e.index));
        }

        private void OnItemRemoved((int index, T item) e)
        {
            if (!_suppressItemEvents)
                Dispatch(new ListChange(CollectionChangeKind.Remove, e.index));
        }

        private void OnItemUpdated((int index, T item) oldItem, (int index, T item) newItem)
        {
            if (!_suppressItemEvents)
                Dispatch(new ListChange(CollectionChangeKind.Update, newItem.index));
        }

        IEnumerator IBindableCollectionUntyped.GetUntypedEnumerator() => _list.GetEnumerator();

        bool IBindableListUntyped.TryGetUntyped(int index, out object item)
        {
            if (TryGet<T>(index).HasResult(out var value))
            {
                item = value;
                return true;
            }
            item = null;
            return false;
        }
    }
}
