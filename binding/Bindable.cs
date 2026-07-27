using System;
using cfEngine;
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
        private Subscription _sourceSub;

        public Bindable(T initialValue = default)
        {
            _prop = new ReactiveProperty<T>(initialValue);
        }

        public T Value
        {
            get => _prop.Value;
            set
            {
                DetachSource();
                _prop.Value = value;
            }
        }

        /// <summary>Inner property, exposed for debug tooling (Rx tag resolution).</summary>
        public ReactiveProperty<T> Rx => _prop;

        /// <summary>
        /// Mirrors a source property: applies its current value immediately and
        /// forwards subsequent changes. Last-write-wins: setting <see cref="Value"/>
        /// locally detaches the source. Scalar sources are only unsubscribed on
        /// detach (never disposed).
        /// </summary>
        public void SetSource(ReactiveProperty<T> source)
        {
            if (source == null)
            {
                ClearSource();
                return;
            }

            DetachSource(warn: false);
            _sourceSub = source.Subscribe(v => _prop.Value = v);
        }

        /// <summary>Detaches the current source without changing the value.</summary>
        public void ClearSource() => DetachSource(warn: false);

        private void DetachSource(bool warn = true)
        {
            if (_sourceSub == null)
                return;
            if (warn)
                cfEngine.Log.LogWarning($"[Bindable<{typeof(T).Name}>] Local set detached the Rx source");
            _sourceSub.Unsubscribe();
            _sourceSub = null;
        }

        public Subscription Subscribe(Action<T> handler) => _prop.Subscribe(handler);

        public Variant GetVariant() => Variant.From(_prop.Value);

        public object GetObject() => _prop.Value;

        public Subscription SubscribeChanged(Action handler) => _prop.Subscribe(_ => handler());
    }
}
