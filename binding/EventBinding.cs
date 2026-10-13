using System;
using cfEngine;
using Godot;

namespace cfGodotEngine.Binding
{
    /// <summary>
    /// Co-located event-binding node. Add as a child of the control whose signal
    /// should drive a named event on the nearest <see cref="IBindingSource"/> that
    /// also implements <see cref="IEventSource"/>. Target convention mirrors
    /// <see cref="Binding"/>: the watched node is always the parent (no NodePath).
    ///
    /// Intentionally NOT <c>[Tool]</c> (the attribute is not inherited — <see cref="Binding"/>
    /// re-declares it for that reason): an event drives game-state mutation, so it
    /// must never fire from the editor. <see cref="ValidateBinding"/> still serves the
    /// inspector drawer, and the emit path early-outs under
    /// <see cref="Engine.IsEditorHint"/> as belt-and-braces.
    /// </summary>
    [GlobalClass]
    [CustomInspector]
    public partial class EventBinding : BindingBase
    {
        /// <summary>Signal on the parent node to observe; defaults to a button press.</summary>
        [Export] private StringName _signal = "pressed";

        /// <summary>Optional payload forwarded to the source event (object channel).</summary>
        [Export] private Variant _arg;

        private Callable _onSignalCallable;
        private Node _connectedTarget;

        // _changeSub (inherited) is intentionally unused: an emitter subscribes to no
        // property changes — it only connects a signal and forwards emits.

        protected override void ResolveAndApply()
        {
            // Belt-and-braces against any future [Tool] subclass: an editor press must
            // never mutate game state.
            if (Engine.IsEditorHint())
                return;

            if (!TryResolveSource())
            {
#if DEBUG
                Log.LogWarning($"[EventBinding:{SafePath(this)}] No IBindingSource found in parent tree. Binding is inactive.");
#endif
                return;
            }

            if (_key == null || _key.IsEmpty)
            {
#if DEBUG
                Log.LogWarning($"[EventBinding:{SafePath(this)}] Key is empty. Source '{_bindingSource.GetType().Name}' was resolved but no key was assigned.");
#endif
                return;
            }

            if (_bindingSource is not IEventSource eventSource)
            {
#if DEBUG
                Log.LogWarning($"[EventBinding:{SafePath(this)}] Source '{_bindingSource.GetType().Name}' does not implement IEventSource. Binding is inactive.");
#endif
                // Authoring error, not pending: do not re-attempt on every tree entry.
                _resolved = true;
                return;
            }

            var map = eventSource.GetEvents;
            if (map == null)
            {
                // Pending: the source was resolved before its event map was built.
                // Stay unresolved and silent so a later Refresh retries (mirrors the
                // null-PropertyMap rule in BindingBase.GetReadyPropertyMap).
                _resolved = false;
                return;
            }

            if (!ContainsKey(map.Keys, _key.ToString()))
            {
#if DEBUG
                Log.LogWarning($"[EventBinding:{SafePath(this)}] Key '{_key}' is not exposed by source '{_bindingSource.GetType().Name}'. Available: [{string.Join(", ", map.Keys)}]. Binding still connects; emits are a no-op until the key exists.");
#endif
            }

            var target = GetParentOrNull<Node>();
            if (target == null)
            {
#if DEBUG
                Log.LogWarning($"[EventBinding:{SafePath(this)}] Parent (target) is null; cannot connect.");
#endif
                return;
            }

            if (!target.HasSignal(_signal))
            {
#if DEBUG
                Log.LogWarning($"[EventBinding:{SafePath(this)}] Parent '{SafePath(target)}' ({target.GetType().Name}) has no signal '{_signal}'. Binding is inactive.");
#endif
                return;
            }

            _onSignalCallable = Callable.From(OnParentSignal);
            if (!target.IsConnected(_signal, _onSignalCallable))
                target.Connect(_signal, _onSignalCallable);
            _connectedTarget = target;
            _resolved = true;

            BindingDebug.LogVerbose($"[EventBinding:{SafePath(this)}] Connected '{_signal}' on '{SafePath(target)}' to event '{_key}'.");
        }

        protected override void Unsubscribe()
        {
            if (_connectedTarget != null && _connectedTarget.IsConnected(_signal, _onSignalCallable))
                _connectedTarget.Disconnect(_signal, _onSignalCallable);

            _connectedTarget = null;
            base.Unsubscribe();
        }

        /// <summary>
        /// Handles the observed parent signal. Emitted signal arguments are ignored: the
        /// payload comes solely from <see cref="_arg"/> (primary contract is the
        /// zero-argument <c>pressed</c> signal).
        /// </summary>
        private void OnParentSignal()
        {
            if (Engine.IsEditorHint())
                return;

            var map = (_bindingSource as IEventSource)?.GetEvents;
            if (map == null)
                return;

            try
            {
                map.TryEmit(_key.ToString(), UnwrapArg(_arg));
            }
            catch (Exception ex)
            {
                Log.LogException(ex, $"[EventBinding:{SafePath(this)}] Event '{_key}' handler threw. Other bindings on this source are NOT affected.");
            }
        }

        /// <summary>Engine-side only: unwraps the exported argument Variant to the object channel.</summary>
        private static object UnwrapArg(Variant arg)
        {
            return arg.VariantType == Variant.Type.Nil ? null : arg.Obj;
        }

        public override string ValidateBinding()
        {
            if (_key == null || _key.IsEmpty)
                return "Key is not set";

            var source = FindBindingSource();
            if (source == null)
                return "No IBindingSource found in parent tree";

            if (source is not IEventSource eventSource)
                return $"Source '{source.GetType().Name}' does not implement IEventSource";

            var map = eventSource.GetEvents;
            if (map == null)
                return string.Empty; // pending is not an authoring error

            var keys = map.Keys;
            if (keys != null && !ContainsKey(keys, _key.ToString()))
                return $"Key '{_key}' is not exposed by source '{source.GetType().Name}'";

            var target = GetParentOrNull<Node>();
            if (target == null)
                return "EventBinding has no parent node (signal target)";

            if (_signal == null || _signal.IsEmpty)
                return "Signal name is not set";

            if (!target.HasSignal(_signal))
                return $"Parent '{target.GetType().Name}' has no signal '{_signal}'";

            return string.Empty;
        }

        private static bool ContainsKey(System.Collections.Generic.IReadOnlyList<string> keys, string key)
        {
            foreach (var k in keys)
            {
                if (k == key)
                    return true;
            }

            return false;
        }
    }
}
