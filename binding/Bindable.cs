using System;
using cfEngine.Rx;
using Godot;

namespace cfGodotEngine.Binding
{
    /// <summary>
    /// Godot-aware wrapper over <see cref="ReactiveProperty{T}"/> for scalar
    /// values. T is constrained to Variant-compatible types at compile time,
    /// so <see cref="GetVariant"/> can never fail.
    /// </summary>
    public class Bindable<[MustBeVariant] T> : IBindable
    {
        private readonly ReactiveProperty<T> _prop;

        public Bindable(T initialValue = default)
        {
            _prop = new ReactiveProperty<T>(initialValue);
        }

        public T Value
        {
            get => _prop.Value;
            set => _prop.Value = value;
        }

        /// <summary>Inner property, exposed for debug tooling (Rx tag resolution).</summary>
        public ReactiveProperty<T> Rx => _prop;

        public Subscription Subscribe(Action<T> handler) => _prop.Subscribe(handler);

        public Variant GetVariant() => Variant.From(_prop.Value);

        public object GetObject() => _prop.Value;

        public Subscription SubscribeChanged(Action handler) => _prop.Subscribe(_ => handler());
    }
}
