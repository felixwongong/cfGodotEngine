using System.Collections.Generic;

namespace cfGodotEngine.Binding
{
    /// <summary>
    /// Hand-authored aggregation of a source's <see cref="BindableEvent"/> /
    /// <see cref="BindableEvent{T}"/> members, keyed by the name a designer picks on
    /// an <see cref="EventBinding"/> node. The event analogue of
    /// <see cref="IPropertyMap"/> — kept separate so events can never appear in the
    /// property map or the binding-key list.
    /// </summary>
    public interface IEventMap
    {
        /// <summary>
        /// Emits the event named <paramref name="key"/> with
        /// <paramref name="payload"/> over the object channel. Returns <c>false</c>
        /// for an unknown key without throwing; handler and payload-conversion
        /// exceptions propagate to the caller (the binding node contains them).
        /// </summary>
        public bool TryEmit(string key, object payload);

        /// <summary>Snapshot of the exposed event keys.</summary>
        public IReadOnlyList<string> Keys { get; }
    }
}
