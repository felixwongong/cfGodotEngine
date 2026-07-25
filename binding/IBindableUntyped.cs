using System.Collections;

namespace cfGodotEngine.Binding
{
    /// <summary>
    /// Framework-internal untyped access to collections. This is the single
    /// sanctioned erasure point: only binding nodes (CollectionBinding,
    /// KeyedCollectionBinding) consume it; game code and designers never see
    /// object-typed returns. Implemented explicitly by the bindable wrappers.
    /// </summary>
    internal interface IBindableCollectionUntyped
    {
        IEnumerator GetUntypedEnumerator();
    }

    internal interface IBindableListUntyped : IBindableCollectionUntyped
    {
        bool TryGetUntyped(int index, out object item);
    }

    internal interface IBindableMapUntyped : IBindableCollectionUntyped
    {
        IEnumerable GetUntypedKeys();
        bool TryGetUntyped(object key, out object value);
    }
}
