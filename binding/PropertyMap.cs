using System;
using System.Collections.Generic;
using cfEngine.Rx;
using Godot;

namespace cfGodotEngine.Binding
{
    public class PropertyMapBuilder
    {
        private readonly List<(string key, IBindable bindable)> _entries = new();

        public PropertyMapBuilder Add(string key, IBindable bindable)
        {
            _entries.Add((key, bindable));
            return this;
        }

        public PropertyMap Build() => new(_entries);
    }

    /// <summary>
    /// Aggregates <see cref="IBindable"/> entries into a single
    /// <see cref="IPropertyMap"/>. Handles per-source batched updates
    /// (replaces the old global <c>BindingUpdateScheduler</c>).
    /// </summary>
    public class PropertyMap : IPropertyMap, IDisposable
    {
        private readonly Dictionary<string, IBindable> _properties;
        private readonly Relay<string> _keyChangedRelay;
        private readonly List<Subscription> _bindableSubs;

        private int _updateDepth;
        private HashSet<string> _pending;
        private bool _disposed;

        internal PropertyMap(List<(string key, IBindable bindable)> entries)
        {
            _properties = new Dictionary<string, IBindable>(entries.Count);
            _keyChangedRelay = new Relay<string>(this);
            _bindableSubs = new List<Subscription>(entries.Count);

            foreach (var (key, bindable) in entries)
            {
                _properties[key] = bindable;
                _bindableSubs.Add(bindable.SubscribeChanged(() => OnPropertyChanged(key)));
            }
        }

        public static PropertyMapBuilder Build() => new();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var sub in _bindableSubs)
            {
                sub?.Unsubscribe();
            }
            _bindableSubs.Clear();
            _keyChangedRelay.RemoveAll();
            _pending?.Clear();
        }

        public bool GetVariant(string key, out Variant value)
        {
            if (_properties.TryGetValue(key, out var bindable))
            {
                value = bindable.GetVariant();
                return true;
            }
            value = default;
            return false;
        }

        public bool GetObject(string key, out object value)
        {
            if (_properties.TryGetValue(key, out var bindable))
            {
                value = bindable.GetObject();
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
