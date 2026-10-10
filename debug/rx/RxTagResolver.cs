using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using cfEngine.Rx;
using cfGodotEngine.Binding;

namespace cfGodotEngine.Debug
{
    /// <summary>
    /// Resolves a Relay owner object to a designer-facing name.
    /// Tags can be pre-registered (fast path) or discovered via reflection
    /// (bindable fields on an owner). Falls back to the owner's runtime type name.
    /// </summary>
    public static class RxTagResolver
    {
        private static readonly ConditionalWeakTable<object, string> _tagCache = new();

        public static string ResolveTag(object owner)
        {
            if (owner == null) return "null";
            if (_tagCache.TryGetValue(owner, out var cached)) return cached;

            var tag = ResolveTagSlow(owner);
            _tagCache.AddOrUpdate(owner, tag);
            return tag;
        }

        /// <summary>Pre-register a known owner-to-tag mapping. Fast path; safe to call repeatedly.</summary>
        public static void RegisterTag(object owner, string tag)
        {
            if (owner == null || string.IsNullOrEmpty(tag)) return;
            _tagCache.AddOrUpdate(owner, tag);
        }

        /// <summary>
        /// Scans all fields of {owner} for ReactiveProperty&lt;T&gt; and IBindable
        /// instances and registers them under their field name. For Bindable&lt;T&gt;
        /// the inner ReactiveProperty (the actual relay owner) is registered too.
        /// Call this in _Ready().
        /// </summary>
        public static void RegisterOwner(object owner)
        {
            if (owner == null) return;
            var type = owner.GetType();
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (var field in fields)
            {
                var value = field.GetValue(owner);
                if (value == null) continue;

                var fieldType = field.FieldType;
                if (fieldType.IsGenericType &&
                    fieldType.GetGenericTypeDefinition() == typeof(ReactiveProperty<>))
                {
                    RegisterTag(value, field.Name);
                    continue;
                }

                if (value is not IBindable) continue;

                RegisterTag(value, field.Name);

                if (fieldType.IsGenericType &&
                    fieldType.GetGenericTypeDefinition() == typeof(Bindable<>))
                {
                    var rx = fieldType.GetProperty(nameof(Bindable<int>.Rx))?.GetValue(value);
                    if (rx != null)
                        RegisterTag(rx, field.Name);
                }
            }
        }

        private static string ResolveTagSlow(object owner)
        {
            return owner.GetType().Name;
        }
    }
}
