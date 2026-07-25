namespace cfGodotEngine.Binding
{
    /// <summary>
    /// Delivery helpers shared by collection bindings. Direct source delivery is
    /// attempted first: when the entry is an <see cref="IBindingSource"/> and the row
    /// implements <see cref="ISourceReceiver"/>, the entry becomes the row's binding
    /// source with no Variant marshalling (pure C# DTO entries supported).
    /// </summary>
    public static class CollectionItemDelivery
    {
        public static bool TrySetSource(object row, object data)
        {
            if (data is IBindingSource source && row is ISourceReceiver receiver)
            {
                receiver.SetSource(source);
                return true;
            }
            return false;
        }
    }
}
