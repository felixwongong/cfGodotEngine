using System;

namespace cfGodotEngine.Binding
{
    /// <summary>
    /// Marks a class as a binding source. The source generator scans for
    /// <c>public readonly ReactiveProperty&lt;T&gt;</c> fields on marked classes
    /// and emits the <see cref="IBindingSource"/> implementation.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class BindingSourceAttribute : Attribute
    {
    }
}
