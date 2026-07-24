using System;
using cfEngine.Rx;
using Godot;

namespace cfGodotEngine.Binding
{
    /// <summary>
    /// A bindable value exposed by an IBindingSource. Bridges a reactive
    /// primitive (ReactiveProperty / RxList / RxDictionary / RxGroup) to the
    /// Godot binding layer: the Variant channel feeds scalar <see cref="Binding"/>
    /// nodes, the object channel feeds <see cref="CollectionBinding"/>, and
    /// SubscribeChanged provides a coarse any-change notification.
    /// </summary>
    public interface IBindable
    {
        Variant GetVariant();
        object GetObject();
        Subscription SubscribeChanged(Action handler);
    }
}
