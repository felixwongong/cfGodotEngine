namespace cfGodotEngine.Binding
{
    /// <summary>
    /// Implemented by nodes able to receive an <see cref="IBindingSource"/> as their
    /// binding source (e.g. UIComponent roots of collection item prefabs). Collection
    /// bindings prefer this direct delivery over Variant-marshalled setter calls,
    /// allowing pure C# DTO entries (e.g. [BindingSource] classes) to flow to rows.
    /// </summary>
    public interface ISourceReceiver
    {
        void SetSource(IBindingSource source);
    }
}
