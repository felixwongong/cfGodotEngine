using System;
using System.Collections.Generic;

namespace cfGodotEngine.Binding
{
    /// <summary>
    /// Fluent builder for an <see cref="EventMap"/>. Mirrors
    /// <see cref="PropertyMapBuilder"/>, but fails fast: events are hand-authored
    /// verbs, so a null key/entry or a duplicate key is an authoring bug and is
    /// rejected at <see cref="Add(string, BindableEvent)"/> time (unlike
    /// <see cref="PropertyMapBuilder"/>, which silently lets a later key win).
    /// </summary>
    public class EventMapBuilder
    {
        private readonly List<(string key, IBindableEventUntyped evt)> _entries = new();

        /// <summary>Adds a payload-less event under <paramref name="key"/>.</summary>
        public EventMapBuilder Add(string key, BindableEvent evt)
        {
            AddUntyped(key, evt);
            return this;
        }

        /// <summary>Adds a typed event under <paramref name="key"/>.</summary>
        public EventMapBuilder Add<T>(string key, BindableEvent<T> evt)
        {
            AddUntyped(key, evt);
            return this;
        }

        public EventMap Build() => new(_entries);

        private void AddUntyped(string key, IBindableEventUntyped evt)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));
            if (evt == null)
                throw new ArgumentNullException(nameof(evt));

            foreach (var (existing, _) in _entries)
            {
                if (existing == key)
                    throw new ArgumentException($"Duplicate event key '{key}'.", nameof(key));
            }

            _entries.Add((key, evt));
        }
    }

    /// <summary>
    /// Aggregates a source's events into a single <see cref="IEventMap"/>, the
    /// event analogue of <see cref="PropertyMap"/>. Unlike <see cref="PropertyMap"/>
    /// it subscribes to nothing and is not <see cref="IDisposable"/>: events own
    /// their own relays, so the map is a pure key → emit-channel index.
    /// </summary>
    public class EventMap : IEventMap
    {
        private readonly Dictionary<string, IBindableEventUntyped> _events;

        internal EventMap(List<(string key, IBindableEventUntyped evt)> entries)
        {
            _events = new Dictionary<string, IBindableEventUntyped>(entries.Count);
            foreach (var (key, evt) in entries)
                _events[key] = evt;
        }

        /// <summary>Entry point mirroring <see cref="PropertyMap.Build"/>.</summary>
        public static EventMapBuilder Build() => new();

        public bool TryEmit(string key, object payload)
        {
            if (key == null || !_events.TryGetValue(key, out var evt))
                return false;

            evt.EmitUntyped(payload);
            return true;
        }

        public IReadOnlyList<string> Keys
        {
            get
            {
                var keys = new string[_events.Count];
                _events.Keys.CopyTo(keys, 0);
                return keys;
            }
        }
    }
}
