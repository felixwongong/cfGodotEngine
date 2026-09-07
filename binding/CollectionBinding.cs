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
        [Export] protected PackedScene _itemPrefab;

        [Export] protected StringName _itemSetterMethod = "SetData";

        protected IBindableList _list;
        private protected IBindableListUntyped _listUntyped;
        protected readonly List<Node> _rows = new();
        protected bool _warnedMissingSetter;
        protected bool _warnedIncompatibleItem;

        protected override void ResolveAndApply()
        {
            if (!TryResolveKey("CollectionBinding", out var map))
                return;

            if (!map.GetCollection(_key.ToString(), out var collection))
            {
#if DEBUG
                Log.LogWarning($"[CollectionBinding:{SafePath(this)}] Key '{_key}' is not a collection on source '{_bindingSource.GetType().Name}'. Available: [{string.Join(", ", map.Keys)}].");
#endif
                return;
            }

            if (collection is not IBindableList list)
            {
#if DEBUG
                Log.LogWarning($"[CollectionBinding:{SafePath(this)}] Key '{_key}' on source '{_bindingSource.GetType().Name}' is not a positional collection (IBindableList). Use KeyedCollectionBinding for keyed collections.");
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

        protected virtual void OnListChanged(ListChange change)
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

        protected virtual void RebuildAll()
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

        protected virtual void InsertRow(int index)
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

        protected virtual void RemoveRow(int index)
        {
            if ((uint)index >= (uint)_rows.Count)
                return;

            _rows[index].QueueFree();
            _rows.RemoveAt(index);
        }

        protected virtual void UpdateRow(int index)
        {
            if ((uint)index >= (uint)_rows.Count || _listUntyped == null)
                return;

            if (!_listUntyped.TryGetUntyped(index, out var item))
                return;

            SetItemData(_rows[index], item);
        }

        protected virtual void ClearRows()
        {
            foreach (var row in _rows)
            {
                if (IsInstanceValid(row))
                    row.QueueFree();
            }
            _rows.Clear();
        }

        protected virtual void SetItemData(Node item, object data)
        {
            var outcome = CollectionItemDelivery.Deliver(item, data, _itemSetterMethod);
            switch (outcome)
            {
                case CollectionItemDelivery.ItemDeliveryOutcome.MissingSetter:
#if DEBUG
                    if (!_warnedMissingSetter)
                    {
                        _warnedMissingSetter = true;
                        Log.LogWarning($"[CollectionBinding:{SafePath(this)}] Row '{SafePath(item)}' has no setter method '{_itemSetterMethod}'; items of key '{_key}' cannot reach their rows.");
                    }
#endif
                    break;
                case CollectionItemDelivery.ItemDeliveryOutcome.SetterCalledWithNil:
#if DEBUG
                    if (!_warnedIncompatibleItem)
                    {
                        _warnedIncompatibleItem = true;
                        Log.LogWarning($"[CollectionBinding:{SafePath(this)}] Item of type '{data.GetType().Name}' (key '{_key}') is not Variant-compatible; row '{SafePath(item)}' receives nil. Use a Variant-compatible item type, an IBindingSource item, or the future object-data path.");
                    }
#endif
                    break;
            }
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
