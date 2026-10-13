using System.Collections.Generic;

namespace cfGodotEngine.Binding
{
    /// <summary>
    /// Hand-authored counterpart to the generator-emitted <see cref="IBindingSource"/>
    /// implementation: a source opts in by implementing <see cref="IEventSource"/>
    /// beside its <c>[BindingSource]</c> attribute and exposing its events through
    /// an <see cref="IEventMap"/>. There is deliberately no <c>static abstract</c>
    /// member here (unlike <see cref="IBindingSource.BindingKeys"/>): event maps are
    /// hand-authored, because the generator that would emit them lives in a
    /// different subtree and has no event support yet.
    /// </summary>
    public interface IEventSource
    {
        /// <summary>
        /// The keys this source exposes for event bindings. Backed by the same
        /// hand-authored map as <see cref="GetEvents"/>.
        /// </summary>
        public IEnumerable<string> GetEventKeys();

        /// <summary>
        /// The source's event map, or <c>null</c> while the source is not ready
        /// (pending semantics, mirroring the null-<see cref="IPropertyMap"/> rule).
        /// </summary>
        public IEventMap GetEvents { get; }
    }
}
