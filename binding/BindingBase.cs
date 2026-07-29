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
    }
}
