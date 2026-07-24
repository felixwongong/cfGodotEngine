using System;
using System.Collections;
using System.Collections.Generic;
using cfEngine.Rx;
using Godot;

namespace cfGodotEngine.Binding
{
    /// <summary>
    /// Godot-aware bindable list over a stable <see cref="RxList{T}"/> instance.
    /// The inner list identity never changes; mutations dispatch granular
    /// collection events plus a coarse change notification for binding.
    /// Items travel via <see cref="GetObject"/> (IEnumerable), never as Variant;
    /// <see cref="GetVariant"/> returns the item count.
    /// </summary>
    public class BindableList<T> : IBindable, IReadOnlyList<T>, IDisposable
    {
        private readonly RxList<T> _list;

        public BindableList()
        {
            _list = new RxList<T>();
        }

        public BindableList(IEnumerable<T> items)
        {
            _list = new RxList<T>(items);
        }

        /// <summary>Read-only face of the inner list (e.g. to feed a BindableGroup).</summary>
        public RxReadOnlyList<T> Source => _list;

        public ICollectionEvents<(int index, T item)> CollectionEvents => _list.Events;

        public int Count => _list.Count;

        public T this[int index] => _list[index];

        public void Add(T item) => _list.Add(item);

        public void Insert(int index, T item) => _list.Insert(index, item);

        public bool Remove(T item) => _list.Remove(item);

        public void RemoveAt(int index) => _list.RemoveAt(index);

        public void Update(int index, T item) => _list.Update(index, item);

        public void Clear() => _list.Clear();

        public void SetAll(IEnumerable<T> items)
        {
            _list.Clear();
            foreach (var item in items)
                _list.Add(item);
        }

        public IEnumerator<T> GetEnumerator() => _list.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public Variant GetVariant() => _list.Count;

        public object GetObject() => _list;

        public Subscription SubscribeChanged(Action handler) => _list.Events.OnChange(handler);

        public void Dispose() => _list.Dispose();
    }
}
