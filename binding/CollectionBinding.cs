using System;
using System.Collections;
using System.Collections.Generic;
using cfEngine;
using cfEngine.Rx;
using Godot;

namespace cfGodotEngine.Binding
{
    /// <summary>
    /// Co-located collection binding node for positional collections
    /// (<see cref="IBindableList"/>). Add as a child of the container node
    /// that should display one item prefab per element. Applies granular
    /// per-row operations: Add inserts a row, Remove frees it, Update
    /// re-delivers data in place, Reset rebuilds all rows.
    /// </summary>
    [Tool]
    [GlobalClass]
    [CustomInspector]
    public partial class CollectionBinding : BindingBase
    {
        [Export] private NodePath _mockSource;

        [Export] private PackedScene _itemPrefab;

        [Export] private StringName _itemSetterMethod = "SetData";

        private IBindableList _list;
        private IBindableListUntyped _listUntyped;
        private readonly List<Node> _rows = new();
        private bool _warnedMissingSetter;
        private bool _warnedIncompatibleItem;

        public override IBindingSource FindBindingSource()
        {
            if (_mockSource != null && !_mockSource.IsEmpty)
            {
                var node = GetNodeOrNull<Node>(_mockSource);
                if (node is IBindingSource mockSource)
                    return mockSource;
            }

            return base.FindBindingSource();
        }

        protected override void ResolveAndApply()
        {
            if (!TryResolveSource())
            {
#if DEBUG
                Log.LogWarning($"[CollectionBinding:{GetPath()}] No IBindingSource found in parent tree. Binding is inactive.");
#endif
                return;
            }

            if (_key == null || _key.IsEmpty)
            {
#if DEBUG
                Log.LogWarning($"[CollectionBinding:{GetPath()}] Key is empty. Source '{_bindingSource.GetType().Name}' was resolved but no key was assigned.");
#endif
                return;
            }

            var map = _bindingSource.GetBindings;
            if (map == null || !map.GetCollection(_key.ToString(), out var collection))
            {
#if DEBUG
                Log.LogWarning($"[CollectionBinding:{GetPath()}] Key '{_key}' is not a collection on source '{_bindingSource.GetType().Name}'. Available: [{string.Join(", ", map?.Keys ?? Array.Empty<string>())}].");
#endif
                return;
            }

            if (collection is not IBindableList list)
            {
#if DEBUG
                Log.LogWarning($"[CollectionBinding:{GetPath()}] Key '{_key}' on source '{_bindingSource.GetType().Name}' is not a positional collection (IBindableList). Use KeyedCollectionBinding for keyed collections.");
#endif
                return;
            }

            _list = list;
            _listUntyped = list as IBindableListUntyped;
            RebuildAll();
            _changeSub = _list.SubscribeChanged(OnListChanged);
            _resolved = true;
        }

        protected override void Unsubscribe()
        {
            base.Unsubscribe();
            _list = null;
            _listUntyped = null;
        }

        private void OnListChanged(ListChange change)
        {
            switch (change.Kind)
            {
                case CollectionChangeKind.Add:
                    InsertRow(change.Index);
                    break;
                case CollectionChangeKind.Remove:
                    RemoveRow(change.Index);
                    break;
                case CollectionChangeKind.Update:
                    UpdateRow(change.Index);
                    break;
                case CollectionChangeKind.Reset:
                    RebuildAll();
                    break;
            }
        }

        private void RebuildAll()
        {
            ClearRows();

            var container = GetParentOrNull<Node>();
            if (container == null || _itemPrefab == null || _listUntyped == null)
                return;

            var enumerator = _listUntyped.GetUntypedEnumerator();
            while (enumerator.MoveNext())
            {
                var row = _itemPrefab.Instantiate<Node>();
                SetItemData(row, enumerator.Current);
                container.AddChild(row);
                _rows.Add(row);
            }
        }

        private void InsertRow(int index)
        {
            var container = GetParentOrNull<Node>();
            if (container == null || _itemPrefab == null || _listUntyped == null)
                return;

            if (!_listUntyped.TryGetUntyped(index, out var item))
                return;

            var row = _itemPrefab.Instantiate<Node>();
            SetItemData(row, item);
            container.AddChild(row);
            _rows.Insert(index, row);
            container.MoveChild(row, container.GetChildCount() - _rows.Count + index);
        }

        private void RemoveRow(int index)
        {
            if ((uint)index >= (uint)_rows.Count)
                return;

            _rows[index].QueueFree();
            _rows.RemoveAt(index);
        }

        private void UpdateRow(int index)
        {
            if ((uint)index >= (uint)_rows.Count || _listUntyped == null)
                return;

            if (!_listUntyped.TryGetUntyped(index, out var item))
                return;

            SetItemData(_rows[index], item);
        }

        private void ClearRows()
        {
            foreach (var row in _rows)
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

            if (CollectionItemDelivery.TrySetSource(item, data))
                return;

            if (_itemSetterMethod == null || _itemSetterMethod.IsEmpty || !item.HasMethod(_itemSetterMethod))
            {
#if DEBUG
                if (!_warnedMissingSetter)
                {
                    _warnedMissingSetter = true;
                    Log.LogWarning($"[CollectionBinding:{GetPath()}] Row '{item.GetPath()}' has no setter method '{_itemSetterMethod}'; items of key '{_key}' cannot reach their rows.");
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
                        Log.LogWarning($"[CollectionBinding:{GetPath()}] Item of type '{data.GetType().Name}' (key '{_key}') is not Variant-compatible; row '{item.GetPath()}' receives nil. Use a Variant-compatible item type, an IBindingSource item, or the future object-data path.");
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

        public override string ValidateBinding()
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
    }
}
