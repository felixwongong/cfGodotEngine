using Godot;

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
        public enum ItemDeliveryOutcome
        {
            ItemNull,
            SourceInjected,
            SetterCalled,
            SetterCalledWithNil,
            MissingSetter
        }

        public static bool TrySetSource(object row, object data)
        {
            if (data is IBindingSource source && row is ISourceReceiver receiver)
            {
                receiver.SetSource(source);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Full item → row delivery path shared by CollectionBinding and
        /// KeyedCollectionBinding. Never logs: the caller switches on the outcome and
        /// emits its own warn-once messages so per-class text stays intact.
        /// </summary>
        public static ItemDeliveryOutcome Deliver(Node item, object data, StringName setterMethod)
        {
            if (item == null)
                return ItemDeliveryOutcome.ItemNull;

            if (TrySetSource(item, data))
                return ItemDeliveryOutcome.SourceInjected;

            if (setterMethod == null || setterMethod.IsEmpty || !item.HasMethod(setterMethod))
                return ItemDeliveryOutcome.MissingSetter;

            if (data != null)
            {
                var variant = ToVariant(data);
                item.Call(setterMethod, variant);
                return variant.VariantType == Variant.Type.Nil
                    ? ItemDeliveryOutcome.SetterCalledWithNil
                    : ItemDeliveryOutcome.SetterCalled;
            }

            item.Call(setterMethod, default(Variant));
            return ItemDeliveryOutcome.SetterCalled;
        }

        internal static Variant ToVariant(object value)
        {
            return value switch
            {
                null => default(Variant),
                int i => i,
                float f => f,
                bool b => b,
                string s => s,
                double d => d,
                long l => l,
                GodotObject g => g,
                _ => default(Variant)
            };
        }
    }
}
