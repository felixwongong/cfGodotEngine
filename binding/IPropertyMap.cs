using System;
using System.Collections.Generic;
using cfEngine.Rx;
using Godot;

namespace cfGodotEngine.Binding
{
    /// <summary>
    /// Godot-aware property map interface. Sources expose their reactive
    /// properties through this interface so that <see cref="Binding"/> nodes
    /// can read values as <see cref="Variant"/> and subscribe to key changes.
    /// </summary>
    public interface IPropertyMap
    {
        public bool GetVariant(string key, out Variant value);

        public bool GetObject(string key, out object value);

        public IReadOnlyList<string> Keys { get; }

        public Subscription RegisterPropertyChange(Action<string> callback);
    }
}
