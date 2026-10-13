using System;
using cfEngine.Rx;

namespace cfGodotEngine.Binding
{
    /// <summary>
    /// Framework-internal untyped emit channel for events. The single sanctioned
    /// erasure point for events, mirroring the <c>IBindableCollectionUntyped</c>
    /// family in <c>IBindableUntyped.cs</c>: only <see cref="EventMap"/> consumes
    /// it, and game code/designers never see object-typed surfaces.
    /// </summary>
    internal interface IBindableEventUntyped
    {
        void EmitUntyped(object payload);
    }

    /// <summary>
    /// Payload-less event twin of <see cref="Bindable{T}"/>. Deliberately NOT an
    /// <see cref="IBindableBase"/>: events are verbs, not state, so they must
    /// never enter a property map or the binding-key list. A source owns its
    /// event objects exactly like its bindables; subscriptions are
    /// weak-referenced <see cref="Relay"/> subscriptions, so retain the
    /// <see cref="Subscription"/> handle returned by <see cref="Subscribe"/>.
    /// There is no <see cref="IDisposable"/> here — ownership and disposal (if
    /// ever needed) live in the owning map, the <see cref="PropertyMap"/>
    /// precedent.
    /// </summary>
    public class BindableEvent : IBindableEventUntyped
    {
        private readonly Relay _relay;

        public BindableEvent()
        {
            _relay = new Relay(this);
        }

        /// <summary>Subscribes a zero-argument handler. Retain the returned handle.</summary>
        public Subscription Subscribe(Action handler) => _relay.AddListener(handler);

        /// <summary>Notifies every current subscriber synchronously.</summary>
        public void Emit() => _relay.Dispatch();

        /// <summary>Untyped emit; a payload-less event ignores the payload.</summary>
        void IBindableEventUntyped.EmitUntyped(object payload) => Emit();
    }

    /// <summary>
    /// Typed twin of <see cref="BindableEvent"/>. <typeparamref name="T"/> is
    /// unconstrained (no <c>[MustBeVariant]</c>): the primitive never
    /// Variant-ifies — it forwards payloads to handlers unchanged. Not an
    /// <see cref="IBindableBase"/> for the same reason as <see cref="BindableEvent"/>.
    /// </summary>
    public class BindableEvent<T> : IBindableEventUntyped
    {
        private readonly Relay<T> _relay;

        public BindableEvent()
        {
            _relay = new Relay<T>(this);
        }

        /// <summary>Subscribes a typed handler. Retain the returned handle.</summary>
        public Subscription Subscribe(Action<T> handler) => _relay.AddListener(handler);

        /// <summary>Notifies every current subscriber synchronously.</summary>
        public void Emit(T payload) => _relay.Dispatch(payload);

        /// <summary>
        /// Framework-internal emit over the object channel. Payload semantics, in
        /// order: (a) payload already is a <typeparamref name="T"/> — forwarded
        /// as-is (fast path, exact type); (b) <c>null</c> — forwarded as
        /// <c>default(T)</c> only when <typeparamref name="T"/> is null-representable
        /// (a reference type or <see cref="Nullable{T}"/>), otherwise
        /// <see cref="InvalidCastException"/>; (c) otherwise a deliberately narrow
        /// primitive coercion via <see cref="Convert.ChangeType(object, Type)"/> —
        /// this exists because a scene-serialized Variant int arrives as a boxed
        /// <see cref="long"/>. The framework never guesses beyond ChangeType's
        /// documented primitive rules; anything richer is a source-authoring
        /// contract (author the event with the wider type, or convert in the
        /// handler). Conversion and handler exceptions propagate to the caller.
        /// </summary>
        void IBindableEventUntyped.EmitUntyped(object payload)
        {
            if (payload is T typed)
            {
                Emit(typed);
                return;
            }

            if (payload == null)
            {
                if (IsNullRepresentable)
                {
                    Emit(default);
                    return;
                }

                throw new InvalidCastException(
                    $"BindableEvent<{typeof(T).Name}> cannot emit a null payload: {typeof(T).Name} is not null-representable. " +
                    "Author the event as a reference type or Nullable<> to accept null.");
            }

            object converted;
            try
            {
                converted = Convert.ChangeType(payload, typeof(T));
            }
            catch (Exception ex)
            {
                throw new InvalidCastException(
                    $"BindableEvent<{typeof(T).Name}> cannot emit a payload of runtime type '{payload.GetType().Name}': " +
                    $"expected {typeof(T).Name}, or a type Convert.ChangeType can coerce to it.", ex);
            }

            Emit((T)converted);
        }

        /// <summary><c>default(T)</c> is <c>null</c> for a reference type or <see cref="Nullable{T}"/>.</summary>
        private static bool IsNullRepresentable =>
            !typeof(T).IsValueType || Nullable.GetUnderlyingType(typeof(T)) != null;
    }
}
