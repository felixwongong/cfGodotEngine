using System;
using System.Collections.Generic;
using cfEngine;
using cfEngine.Rx;
using Godot;

namespace cfGodotEngine.Binding
{
    /// <summary>
    /// Marker base for values exposed by an IBindingSource: what PropertyMap
    /// stores and what the BindingSourceGenerator matches. Provides the
    /// coarse any-change notification shared by all bindable kinds.
    /// </summary>
    public interface IBindableBase
    {
        Subscription SubscribeChanged(Action handler);
    }

    /// <summary>
    /// Scalar bindable channel, consumed by <see cref="Binding"/> nodes.
    /// The Variant channel feeds Godot properties/methods; the object
    /// channel is the raw-value escape hatch for framework-internal consumers.
    /// </summary>
    public interface IBindable : IBindableBase
    {
        Variant GetVariant();
        object GetObject();
    }

    public enum CollectionChangeKind
    {
        Add,
        Remove,
        Update,
        Reset
    }

    /// <summary>
    /// Positional change notification. Carries the index only; retrieve the
    /// item via <see cref="IBindableList.TryGet{T}"/>. Index is -1 on Reset.
    /// </summary>
    public readonly struct ListChange
    {
        public readonly CollectionChangeKind Kind;
        public readonly int Index;

        public ListChange(CollectionChangeKind kind, int index)
        {
            Kind = kind;
            Index = index;
        }
    }

    /// <summary>
    /// Keyed change notification. The key is held internally and retrieved
    /// through the typed <see cref="Key{TK}"/> accessor — it is never
    /// returned as object. On Reset there is no key (Key returns Err).
    /// </summary>
    public readonly struct MapChange
    {
        public readonly CollectionChangeKind Kind;
        private readonly object _key;

        internal MapChange(CollectionChangeKind kind, object key)
        {
            Kind = kind;
            _key = key;
        }

        public Res<TK, Exception> Key<TK>()
        {
            if (_key is TK key)
                return Res.Ok(key);
            return Res.Err<TK>(new InvalidOperationException($"MapChange has no key of type {typeof(TK).Name} (kind: {Kind})"));
        }

        internal object UntypedKey => _key;
    }

    /// <summary>
    /// Base of the collection bindable channel, consumed by collection
    /// binding nodes. All item access is typed; the framework performs any
    /// erasure internally and never returns object to callers.
    /// </summary>
    public interface IBindableCollection : IBindableBase
    {
        int Count { get; }
        Type ItemType { get; }
        IEnumerable<T> Enumerate<T>();
    }

    /// <summary>
    /// Positional collection channel, consumed by <see cref="CollectionBinding"/>.
    /// Granular changes carry indices only.
    /// </summary>
    public interface IBindableList : IBindableCollection
    {
        Res<T, Exception> TryGet<T>(int index);
        Subscription SubscribeChanged(Action<ListChange> handler);
    }

    /// <summary>
    /// Keyed collection channel, consumed by KeyedCollectionBinding.
    /// Granular changes carry keys only.
    /// </summary>
    public interface IBindableMap : IBindableCollection
    {
        Type KeyType { get; }
        IEnumerable<TK> EnumerateKeys<TK>();
        Res<TV, Exception> TryGet<TK, TV>(TK key);
        Subscription SubscribeChanged(Action<MapChange> handler);
    }
}
