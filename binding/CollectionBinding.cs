using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using cfEngine.Rx;
using Godot;

namespace cfGodotEngine.Binding
{
    /// <summary>
    /// Co-located collection binding node. Add as a child of the container node
    /// that should display one item prefab per element from a reactive
    /// <see cref="IEnumerable"/> key.
    /// </summary>
    [Tool]
    [GlobalClass]
    [CustomInspector]
    public partial class CollectionBinding : Node
    {
        [Export] private StringName _key;

        [Export] private NodePath _sourceOverride;

        [Export] private NodePath _mockSource;

        [Export] private PackedScene _itemPrefab;

        [Export] private StringName _itemSetterMethod = "SetData";

        private IBindingSource _bindingSource;
        private Subscription _changeSub;
        private List<object> _currentItems = new();

        public StringName Key => _key;

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

            Refresh();
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
                Refresh();
        }

        private void Refresh()
        {
            if (_bindingSource == null)
                return;

            var map = _bindingSource.GetBindings;
            if (map == null)
                return;

            if (!map.GetObject(_key.ToString(), out var obj) || obj == null)
                return;

            var collection = obj as IEnumerable;
            if (collection == null)
                return;

            var newItems = new List<object>();
            foreach (var item in collection)
                newItems.Add(item);

            if (SequencesEqual(_currentItems, newItems))
                return;

            ClearItems();

            var container = GetParentOrNull<Node>();
            if (container == null || _itemPrefab == null)
                return;

            foreach (var item in newItems)
            {
                var node = _itemPrefab.Instantiate<Node>();
                container.AddChild(node);
                SetItemData(node, item);
            }

            _currentItems = newItems;
        }

        private void ClearItems()
        {
            var container = GetParentOrNull<Node>();
            if (container == null)
                return;

            foreach (var child in container.GetChildren())
            {
                if (child == this) continue;
                child.QueueFree();
            }

            _currentItems.Clear();
        }

        private void SetItemData(Node item, object data)
        {
            if (item == null || _itemSetterMethod == null || _itemSetterMethod.IsEmpty)
                return;

            if (item.HasMethod(_itemSetterMethod))
            {
                item.Call(_itemSetterMethod, ToVariant(data));
            }
        }

        private static Variant ToVariant(object value)
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

        public IReadOnlyList<string> GetAvailableKeys()
        {
            var source = FindBindingSource();
            if (source == null)
                return Array.Empty<string>();

            return source.GetBindings?.Keys ?? Array.Empty<string>();
        }

        public string ValidateBinding()
        {
            if (_key == null || _key.IsEmpty)
                return "Key is not set";

            var source = FindBindingSource();
            if (source == null)
                return "No IBindingSource found in parent tree";

            if (_itemPrefab == null)
                return "Item prefab is not assigned";

            var container = GetParentOrNull<Node>();
            if (container == null)
                return "CollectionBinding has no parent node (container)";

            return string.Empty;
        }

        private static bool SequencesEqual(List<object> a, List<object> b)
        {
            if (a.Count != b.Count)
                return false;

            for (int i = 0; i < a.Count; i++)
            {
                if (!Equals(a[i], b[i]))
                    return false;
            }

            return true;
        }
    }
}
