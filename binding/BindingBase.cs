using System;
using cfEngine;
using cfEngine.Rx;
using Godot;

namespace cfGodotEngine.Binding
{
    /// <summary>
    /// Shared base for co-located binding nodes. Handles source resolution,
    /// lifecycle subscription, and cleanup. Derived classes implement the
    /// concrete apply logic for scalar properties, positional collections, or
    /// keyed collections.
    /// </summary>
    [Tool]
    public abstract partial class BindingBase : Node
    {
        [Export(PropertyHint.None, "BindingSourceKey")]
        protected StringName _key;

        [Export] protected NodePath _sourceOverride;

        protected IBindingSource _bindingSource;
        protected Subscription _changeSub;
        protected bool _resolved;

        public StringName Key => _key;

        public override void _Ready()
        {
            base._Ready();
            if (!_resolved)
                ResolveAndApply();
        }

        public override void _EnterTree()
        {
            base._EnterTree();
            if (!_resolved)
                ResolveAndApply();
        }

        public override void _ExitTree()
        {
            Unsubscribe();
            base._ExitTree();
        }

        public virtual IBindingSource FindBindingSource()
        {
            if (_sourceOverride != null && !_sourceOverride.IsEmpty)
            {
                var node = GetNodeOrNull<Node>(_sourceOverride);
                if (node is IBindingSource overrideSource)
                    return overrideSource;
            }

            for (var x = GetParentOrNull<Node>(); x != null; x = x.GetParentOrNull<Node>())
            {
                if (x is IBindingSource source)
                    return source;
            }

            return null;
        }

        /// <summary>
        /// Re-resolves the source and re-applies. Required when an ancestor's
        /// injected source instance was swapped (e.g. ISourceReceiver.SetSource
        /// delivering a new entry object on a collection Update): existing
        /// subscriptions still point at the old source and would go stale.
        /// </summary>
        public void Refresh()
        {
            Unsubscribe();
            ResolveAndApply();
        }

        /// <summary>Refreshes every binding node in the subtree (depth-first).</summary>
        public static void RefreshDescendants(Node root)
        {
            foreach (var child in root.GetChildren())
            {
                if (child is BindingBase binding)
                    binding.Refresh();
                else
                    RefreshDescendants(child);
            }
        }

        protected abstract void ResolveAndApply();

        protected virtual void Unsubscribe()
        {
            if (_changeSub != null)
            {
                _changeSub.Unsubscribe();
                _changeSub = null;
            }
            _bindingSource = null;
            _resolved = false;
        }

        protected bool TryResolveSource()
        {
            Unsubscribe();
            _bindingSource = FindBindingSource();
            return _bindingSource != null;
        }

        public abstract string ValidateBinding();

        /// <summary>
        /// Binding rows can receive their source before CollectionBinding adds
        /// them to the scene tree. Godot's GetPath() is invalid in that window,
        /// so diagnostics must use a detached-safe fallback.
        /// </summary>
        protected string SafePath(Node node = null)
        {
            if (node == null)
                return "<null>";

            return node.IsInsideTree()
                ? node.GetPath().ToString()
                : $"<detached:{node.Name}>";
        }

        /// <summary>
        /// Returns the resolved source's property map, or null when the source
        /// is not ready yet (row entered the tree before item-source delivery).
        /// A null result means "pending": leave the binding unresolved so a
        /// later SetSource-driven Refresh retries; do not warn.
        /// </summary>
        protected IPropertyMap GetReadyPropertyMap()
        {
            return _bindingSource?.GetBindings;
        }

        protected enum ResolveKeyStatus { NoSource, KeyEmpty, PendingMap }

        /// <summary>
        /// Shared ResolveAndApply prelude: resolve source → key-empty check →
        /// pending-PropertyMap check. Warnings are byte-identical to the former
        /// per-class copies; logTag is caller-supplied so subclasses inheriting a
        /// base's logic keep the base's log tag. A null map is "pending": sets
        /// _resolved = false and returns false with no warning.
        /// </summary>
        protected bool TryResolveKey(string logTag, out IPropertyMap map)
        {
            map = null;

            if (!TryResolveSource())
            {
#if DEBUG
                Log.LogWarning($"[{logTag}:{SafePath(this)}] No IBindingSource found in parent tree. Binding is inactive.");
#endif
                OnResolveKeyFailed(ResolveKeyStatus.NoSource);
                return false;
            }

            if (_key == null || _key.IsEmpty)
            {
#if DEBUG
                Log.LogWarning($"[{logTag}:{SafePath(this)}] Key is empty. Source '{_bindingSource.GetType().Name}' was resolved but no key was assigned.");
#endif
                OnResolveKeyFailed(ResolveKeyStatus.KeyEmpty);
                return false;
            }

            OnKeyResolved();

            map = GetReadyPropertyMap();
            if (map == null)
            {
                OnResolveKeyFailed(ResolveKeyStatus.PendingMap);
                _resolved = false;
                return false;
            }

            return true;
        }

        protected virtual void OnResolveKeyFailed(ResolveKeyStatus status) { }

        /// <summary>Called after source+key resolve, before the PropertyMap fetch.</summary>
        protected virtual void OnKeyResolved() { }
    }
}
