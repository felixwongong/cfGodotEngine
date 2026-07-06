using System;
using cfEngine.DataStructure;
using Godot;

namespace cfGodotEngine.Binding;

/// <summary>
/// Provides sample binding values for editor preview.
/// At runtime binders skip mock sources and resolve the real source instead.
/// </summary>
[Tool]
[GlobalClass]
public partial class MockBindingSource : Node, IBindingSource
{
    [Export]
    public Godot.Collections.Dictionary sampleValues = new();

    private MockPropertyMap _propertyMap;

    public IPropertyMap GetBindings
    {
        get
        {
            if (_propertyMap == null)
                _propertyMap = new MockPropertyMap(this);
            return _propertyMap;
        }
    }

    public IDisposable BeginUpdate() => new EmptyUpdateScope();

    private class EmptyUpdateScope : IDisposable
    {
        public void Dispose()
        {
        }
    }

    private class MockPropertyMap : IPropertyMap
    {
        private readonly MockBindingSource _owner;

        public MockPropertyMap(MockBindingSource owner)
        {
            _owner = owner;
        }

        public bool Get<T>(string key, out T value)
        {
            value = default;
            if (!_owner.sampleValues.TryGetValue((StringName)key, out var variant))
                return false;

            object boxed;
            var targetType = typeof(T);
            if (targetType == typeof(int))
                boxed = variant.AsInt32();
            else if (targetType == typeof(float))
                boxed = variant.AsSingle();
            else if (targetType == typeof(double))
                boxed = variant.AsDouble();
            else if (targetType == typeof(bool))
                boxed = variant.AsBool();
            else if (targetType == typeof(string))
                boxed = variant.AsString();
            else if (targetType == typeof(Color))
                boxed = variant.AsColor();
            else if (targetType == typeof(Vector2))
                boxed = variant.AsVector2();
            else if (targetType == typeof(Texture2D))
                boxed = variant.As<Texture2D>();
            else if (targetType == typeof(Resource))
                boxed = variant.As<Resource>();
            else
                boxed = variant;

            if (boxed is T tValue)
            {
                value = tValue;
                return true;
            }

            return false;
        }

        public void RegisterPropertyChange(Action<string> callback)
        {
        }

        public void UnregisterPropertyChange(Action<string> callback)
        {
        }
    }
}
