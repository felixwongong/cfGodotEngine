using System;
using System.Collections.Generic;

namespace cfGodotEngine.Binding
{
    public interface IBindingSource
    {
        public static abstract IEnumerable<string> BindingKeys { get; }
        public IEnumerable<string> GetBindingKeys();
        public IPropertyMap GetBindings { get; }

        /// <summary>
        /// Begins a batched update scope. Property changes inside the scope are coalesced
        /// and dispatched once when the returned <see cref="IDisposable"/> is disposed.
        /// </summary>
        public IDisposable BeginUpdate();
    }
}
