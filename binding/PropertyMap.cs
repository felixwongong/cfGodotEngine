using System;
using System.Collections.Generic;
using cfEngine.Rx;
using Godot;

namespace cfGodotEngine.Binding
{
    public interface IReactivePropertyAdapter
    {
        Variant GetVariant();
        object GetObject();
        Subscription SubscribeToChange(Action handler);
    }

    public class ReactivePropertyAdapter<T> : IReactivePropertyAdapter
    {
        private readonly ReactiveProperty<T> _prop;
        private readonly string _key;

        public ReactivePropertyAdapter(ReactiveProperty<T> prop, string key)
        {
            _prop = prop;
            _key = key;
        }

        public Variant GetVariant() => Variant.From(_prop.Value);

        public object GetObject() => _prop.GetValue();

        public Subscription SubscribeToChange(Action handler)
        {
            return _prop.Subscribe(_ => handler());
        }
    }

    public class PropertyMapBuilder
    {
        private readonly List<(string key, IReactivePropertyAdapter adapter)> _entries = new();

        public PropertyMapBuilder Add<T>(string key, ReactiveProperty<T> prop)
        {
            _entries.Add((key, new ReactivePropertyAdapter<T>(prop, key)));
            return this;
        }

        public PropertyMap Build() => new(_entries);
    }

    /// <summary>
    /// Aggregates <see cref="ReactiveProperty{T}"/> fields into a single
    /// <see cref="IPropertyMap"/>. Handles per-source batched updates
    /// (replaces the old global <c>BindingUpdateScheduler</c>).
    /// </summary>
    public class PropertyMap : IPropertyMap
    {
        private readonly Dictionary<string, IReactivePropertyAdapter> _properties;
        private readonly Relay<string> _keyChangedRelay;
        private readonly List<Subscription> _adapterSubs;

        private int _updateDepth;
        private HashSet<string> _pending;

        internal PropertyMap(List<(string key, IReactivePropertyAdapter adapter)> entries)
        {
            _properties = new Dictionary<string, IReactivePropertyAdapter>(entries.Count);
            _keyChangedRelay = new Relay<string>(this);
            _adapterSubs = new List<Subscription>(entries.Count);

            foreach (var (key, adapter) in entries)
            {
                _properties[key] = adapter;
                _adapterSubs.Add(adapter.SubscribeToChange(() => OnPropertyChanged(key)));
            }
        }

        public static PropertyMapBuilder Build() => new();

        public bool GetVariant(string key, out Variant value)
        {
            if (_properties.TryGetValue(key, out var adapter))
            {
                value = adapter.GetVariant();
                return true;
            }
            value = default;
            return false;
        }

        public bool GetObject(string key, out object value)
        {
            if (_properties.TryGetValue(key, out var adapter))
            {
                value = adapter.GetObject();
                return true;
            }
            value = null;
            return false;
        }

        public IReadOnlyList<string> Keys
        {
            get
            {
                var keys = new string[_properties.Count];
                _properties.Keys.CopyTo(keys, 0);
                return keys;
            }
        }

        public Subscription RegisterPropertyChange(Action<string> callback)
            => _keyChangedRelay.AddListener(callback);

        public IDisposable BeginUpdate()
        {
            _updateDepth++;
            return new UpdateScope(this);
        }

        private void EndUpdate()
        {
            _updateDepth--;
            if (_updateDepth == 0 && _pending != null)
            {
                foreach (var key in _pending)
                    _keyChangedRelay.Dispatch(key);
                _pending.Clear();
            }
        }

        private void OnPropertyChanged(string key)
        {
            if (_updateDepth > 0)
            {
                _pending ??= new HashSet<string>();
                _pending.Add(key);
            }
            else
            {
                _keyChangedRelay.Dispatch(key);
            }
        }

        private class UpdateScope : IDisposable
        {
            private readonly PropertyMap _map;
            public UpdateScope(PropertyMap map) => _map = map;
            public void Dispose() => _map.EndUpdate();
        }
    }
}
