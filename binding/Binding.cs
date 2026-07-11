using System;
using System.Collections.Generic;
using System.Reflection;
using cfEngine.Rx;
using Godot;

namespace cfGodotEngine.Binding
{
    /// <summary>
    /// Co-located binding node. Add as a child of the UI element whose property
    /// should reflect a reactive property from the nearest <see cref="IBindingSource"/>.
    /// The target is always the parent node — no NodePath needed.
    /// </summary>
    [Tool]
    [GlobalClass]
    [CustomInspector]
    public partial class Binding : Node
    {
        [Export(PropertyHint.None, "BindingSourceKey")]
        private StringName _key;

        [Export] private NodePath _sourceOverride;

        [Export] private NodePath _mockSource;

        [Export(PropertyHint.None, "BindingApplyTo")]
        private StringName _applyTo;

        [Export] private string _format;

        [Export] private BindingConverter[] _converters;

        private IBindingSource _bindingSource;
        private Subscription _changeSub;

        public StringName Key => _key;
        public StringName ApplyTo => _applyTo;

        public override void _Ready()
        {
            base._Ready();
            ResolveAndApply();
        }

        public override void _EnterTree()
        {
            base._EnterTree();
            if (Engine.IsEditorHint())
                ResolveAndApply();
        }

        public override void _ExitTree()
        {
            Unsubscribe();
            base._ExitTree();
        }

        private void ResolveAndApply()
        {
            Unsubscribe();
            _bindingSource = FindBindingSource();
            if (_bindingSource == null)
                return;

            if (_key == null || _key.IsEmpty)
                return;

            ApplyValue();
            _changeSub = _bindingSource.GetBindings.RegisterPropertyChange(OnKeyChanged);
        }

        private void Unsubscribe()
        {
            if (_changeSub != null)
            {
                _changeSub.Unsubscribe();
                _changeSub = null;
            }
            _bindingSource = null;
        }

        private IBindingSource FindBindingSource()
        {
            bool isEditor = Engine.IsEditorHint();

            if (isEditor && _mockSource != null && !_mockSource.IsEmpty)
            {
                var mock = GetNodeOrNull<Node>(_mockSource);
                if (mock is IBindingSource mockSource)
                    return mockSource;
            }

            if (_sourceOverride != null && !_sourceOverride.IsEmpty)
            {
                var node = GetNodeOrNull<Node>(_sourceOverride);
                if (node is IBindingSource overrideSource)
                    return overrideSource;
            }

            for (var x = GetParentOrNull<Node>(); x != null; x = x.GetParentOrNull<Node>())
            {
                if (x is IBindingSource source)
                {
                    if (!isEditor && x is MockBindingSource)
                        continue;
                    return source;
                }
            }

            return null;
        }

        private void OnKeyChanged(string changedKey)
        {
            if (changedKey == _key.ToString())
                ApplyValue();
        }

        private void ApplyValue()
        {
            if (_bindingSource == null)
                return;

            var map = _bindingSource.GetBindings;
            if (map == null)
                return;

            if (!map.GetVariant(_key.ToString(), out var value))
                return;

            var target = GetParentOrNull<Node>();
            if (target == null)
                return;

            if (!string.IsNullOrEmpty(_format))
                value = string.Format(_format, value.ToString());

            if (_converters != null)
            {
                foreach (var converter in _converters)
                {
                    if (converter == null) continue;
                    value = converter.Convert(value);
                }
            }

            if (_applyTo == null || _applyTo.IsEmpty)
                return;

            if (target.HasMethod(_applyTo))
                target.Call(_applyTo, value);
            else
                target.Set(_applyTo, value);
        }

        #region Inspector helpers

        public IReadOnlyList<string> GetAvailableKeys()
        {
            var source = FindBindingSource();
            if (source == null)
                return Array.Empty<string>();

            return source.GetBindings?.Keys ?? Array.Empty<string>();
        }

        public IReadOnlyList<string> GetAvailableApplyTargets()
        {
            var target = GetParentOrNull<Node>();
            if (target == null)
                return Array.Empty<string>();

            var result = new List<string>();

            var propList = target.GetPropertyList();
            foreach (var dict in propList)
            {
                var name = dict["name"].AsString();
                if (!string.IsNullOrEmpty(name))
                    result.Add(name);
            }

            var methods = target.GetMethodList();
            foreach (var dict in methods)
            {
                var name = dict["name"].AsString();
                if (!string.IsNullOrEmpty(name) && !name.StartsWith("_") && !result.Contains(name))
                    result.Add(name);
            }

            result.Sort(StringComparer.Ordinal);
            return result;
        }

        public string ValidateBinding()
        {
            if (_key == null || _key.IsEmpty)
                return "Key is not set";

            var source = FindBindingSource();
            if (source == null)
                return "No IBindingSource found in parent tree";

            var keys = source.GetBindings?.Keys;
            if (keys != null && !ContainsKey(keys, _key.ToString()))
                return $"Key '{_key}' is not exposed by source '{source.GetType().Name}'";

            var target = GetParentOrNull<Node>();
            if (target == null)
                return "Binding has no parent node (target)";

            if (_applyTo == null || _applyTo.IsEmpty)
                return "Apply To is not set";

            return string.Empty;
        }

        private static bool ContainsKey(IReadOnlyList<string> keys, string key)
        {
            foreach (var k in keys)
            {
                if (k == key)
                    return true;
            }
            return false;
        }

        #endregion
    }
}
