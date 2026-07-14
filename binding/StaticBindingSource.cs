using System;
using System.Collections.Generic;
using cfEngine.Rx;
using Godot;

namespace cfGodotEngine.Binding
{
    /// <summary>
    /// Provides sample binding values for editor preview.
    /// At runtime binding nodes skip mock sources and resolve the real source instead.
    /// </summary>
    [Tool]
    [GlobalClass]
    public partial class StaticBindingSource : Node, IBindingSource
    {
        [Export]
        public Godot.Collections.Dictionary sampleValues = new();

        private PropertyMap _propertyMap;

        public IPropertyMap GetBindings
        {
            get
            {
                if (_propertyMap == null)
                    _propertyMap = new PropertyMap(this);
                return _propertyMap;
            }
        }

        public IDisposable BeginUpdate() => new EmptyUpdateScope();

        private class EmptyUpdateScope : IDisposable
        {
            public void Dispose() { }
        }

        private class PropertyMap : IPropertyMap
        {
            private readonly StaticBindingSource _owner;
            private List<string> _keys;

            public PropertyMap(StaticBindingSource owner)
            {
                _owner = owner;
                _keys = new List<string>();
                foreach (var key in _owner.sampleValues.Keys)
                    _keys.Add((string)key);
            }

            public bool GetVariant(string key, out Variant value)
            {
                if (_owner.sampleValues.TryGetValue((StringName)key, out var variant))
                {
                    value = variant;
                    return true;
                }
                value = default;
                return false;
            }

            public bool GetObject(string key, out object value)
            {
                if (_owner.sampleValues.TryGetValue((StringName)key, out var variant))
                {
                    value = variant.Obj;
                    return true;
                }
                value = null;
                return false;
            }

            public IReadOnlyList<string> Keys => _keys;

            public Subscription RegisterPropertyChange(Action<string> callback) => null;
        }
    }
}
