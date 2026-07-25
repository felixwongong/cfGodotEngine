using System;
using System.Collections;
using System.Collections.Generic;
using cfEngine;
using cfEngine.Rx;
using Godot;

namespace cfGodotEngine.Binding
{
    /// <summary>
    /// Co-located collection binding node for keyed collections
    /// (<see cref="IBindableMap"/>). Rows are tracked by key rather than
    /// position: Add creates a row for the key, Remove frees it, Update
    /// re-delivers data in place, Reset rebuilds all rows.
    /// </summary>
    [Tool]
    [GlobalClass]
    [CustomInspector]
    public partial class KeyedCollectionBinding : Node
    {
        [Export] private StringName _key;

        [Export] private NodePath _sourceOverride;

        [Export] private PackedScene _itemPrefab;

        [Export] private StringName _itemSetterMethod = "SetData";

        private IBindingSource _bindingSource;
        private IBindableMap _map;
        private IBindableMapUntyped _mapUntyped;
        private Subscription _changeSub;
        private readonly Dictionary<object, Node> _rows = new();
        private bool _warnedMissingSetter;
        private bool _warnedIncompatibleItem;

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

            var propertyMap = _bindingSource.GetBindings;
            if (propertyMap == null || !propertyMap.GetCollection(_key.ToString(), out var collection))
            {
#if DEBUG
                Log.LogWarning($"[KeyedCollectionBinding:{GetPath()}] Key '{_key}' is not a collection on source '{_bindingSource.GetType().Name}'. Available: [{string.Join(", ", propertyMap?.Keys ?? Array.Empty<string>())}].");
#endif
                return;
            }

            if (collection is not IBindableMap map)
            {
#if DEBUG
                Log.LogWarning($"[KeyedCollectionBinding:{GetPath()}] Key '{_key}' on source '{_bindingSource.GetType().Name}' is not a keyed collection (IBindableMap). Use CollectionBinding for positional collections.");
#endif
                return;
            }

            _map = map;
            _mapUntyped = map as IBindableMapUntyped;
            RebuildAll();
            _changeSub = _map.SubscribeChanged(OnMapChanged);
        }

        private void Unsubscribe()
        {
            if (_changeSub != null)
            {
                _changeSub.Unsubscribe();
                _changeSub = null;
            }
            _map = null;
            _mapUntyped = null;
            _bindingSource = null;
        }

        public IBindingSource FindBindingSource()
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
                {
                    return source;
                }
            }

            return null;
        }

        private void OnMapChanged(MapChange change)
        {
            if (change.Kind == CollectionChangeKind.Reset)
            {
                RebuildAll();
                return;
            }

            var key = change.UntypedKey;
            if (key == null)
                return;

            switch (change.Kind)
            {
                case CollectionChangeKind.Add:
                case CollectionChangeKind.Update:
                    UpsertRow(key);
                    break;
                case CollectionChangeKind.Remove:
                    RemoveRow(key);
                    break;
            }
        }

        private void RebuildAll()
        {
            ClearRows();

            var container = GetParentOrNull<Node>();
            if (container == null || _itemPrefab == null || _mapUntyped == null)
                return;

            foreach (var key in _mapUntyped.GetUntypedKeys())
            {
                if (_mapUntyped.TryGetUntyped(key, out var item))
                    AddRow(container, key, item);
            }
        }

        private void UpsertRow(object key)
        {
            var container = GetParentOrNull<Node>();
            if (container == null || _itemPrefab == null || _mapUntyped == null)
                return;

            if (!_mapUntyped.TryGetUntyped(key, out var item))
                return;

            if (_rows.TryGetValue(key, out var existing) && IsInstanceValid(existing))
            {
                SetItemData(existing, item);
                return;
            }

            AddRow(container, key, item);
        }

        private void AddRow(Node container, object key, object item)
        {
            var row = _itemPrefab.Instantiate<Node>();
            container.AddChild(row);
            _rows[key] = row;
            SetItemData(row, item);
        }

        private void RemoveRow(object key)
        {
            if (!_rows.TryGetValue(key, out var row))
                return;

            if (IsInstanceValid(row))
                row.QueueFree();
            _rows.Remove(key);
        }

        private void ClearRows()
        {
            foreach (var row in _rows.Values)
            {
                if (IsInstanceValid(row))
                    row.QueueFree();
            }
            _rows.Clear();
        }

        private void SetItemData(Node item, object data)
        {
            if (item == null)
                return;

            if (_itemSetterMethod == null || _itemSetterMethod.IsEmpty || !item.HasMethod(_itemSetterMethod))
            {
#if DEBUG
                if (!_warnedMissingSetter)
                {
                    _warnedMissingSetter = true;
                    Log.LogWarning($"[KeyedCollectionBinding:{GetPath()}] Row '{item.GetPath()}' has no setter method '{_itemSetterMethod}'; items of key '{_key}' cannot reach their rows.");
                }
#endif
                return;
            }

            if (data != null)
            {
                var variant = ToVariant(data);
                if (variant.VariantType == Variant.Type.Nil)
                {
#if DEBUG
                    if (!_warnedIncompatibleItem)
                    {
                        _warnedIncompatibleItem = true;
                        Log.LogWarning($"[KeyedCollectionBinding:{GetPath()}] Item of type '{data.GetType().Name}' (key '{_key}') is not Variant-compatible; row '{item.GetPath()}' receives nil.");
                    }
#endif
                }
                item.Call(_itemSetterMethod, variant);
            }
            else
            {
                item.Call(_itemSetterMethod, default(Variant));
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
                return "KeyedCollectionBinding has no parent node (container)";

            return string.Empty;
        }
    }
}
