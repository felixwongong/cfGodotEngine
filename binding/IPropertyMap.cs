using System;
using System.Collections.Generic;
using cfEngine.Rx;
using Godot;

namespace cfGodotEngine.Binding
{
    /// <summary>
    /// Godot-aware property map interface. Sources expose their bindables
    /// through this interface: scalar keys serve <see cref="Binding"/> nodes
    /// via the Variant/object channels, collection keys serve
    /// <see cref="CollectionBinding"/> nodes via the collection channel.
    /// </summary>
    public interface IPropertyMap
    {
        public bool GetVariant(string key, out Variant value);

        public bool GetObject(string key, out object value);

        public bool GetCollection(string key, out IBindableCollection collection);

        public IReadOnlyList<string> Keys { get; }

        public Subscription RegisterPropertyChange(Action<string> callback);
    }
}
