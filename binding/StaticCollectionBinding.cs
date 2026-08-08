using System;
using System.Collections.Generic;
using System.Linq;
using cfEngine;
using Godot;

namespace cfGodotEngine.Binding
{
    /// <summary>
    /// Positional collection binding that reuses item scenes already inline in
    /// the container. Inline rows are marked with the
    /// <c>static_collection_item</c> metadata key (or match the configured
    /// prefab scene path). Rows are added only when the collection exceeds the
    /// available inline rows and removed from the tail when it shrinks.
    /// </summary>
    [Tool]
    [GlobalClass]
    [CustomInspector]
    public partial class StaticCollectionBinding : CollectionBinding
    {
        private const string InlineItemMetadata = "static_collection_item";

        private readonly List<Node> _inlineRows = new();
        private readonly HashSet<Node> _spawnedRows = new();
        private bool _hasObservedItems;
        private bool _reconcileQueued;

        protected override void RebuildAll()
        {
            DiscoverInlineRows();

            // Panels inject their view before the first stage session exists.
            // Keep the designer's inline placeholders until real collection
            // data arrives instead of destroying them during that empty pass.
            if (!_hasObservedItems && _list.Count == 0)
                return;

            _hasObservedItems = true;
            ReconcileRows();
        }

        protected override void OnListChanged(ListChange change)
        {
            _hasObservedItems = true;
            if (_reconcileQueued)
                return;

            _reconcileQueued = true;
            CallDeferred(nameof(ReconcileRowsDeferred));
        }

        protected override void Unsubscribe()
        {
            base.Unsubscribe();

            foreach (var row in _spawnedRows)
            {
                if (IsInstanceValid(row))
                    row.QueueFree();
            }

            _spawnedRows.Clear();
            _rows.Clear();
            _hasObservedItems = false;
            _reconcileQueued = false;
        }

        protected override void ClearRows()
        {
            foreach (var row in _spawnedRows)
            {
                if (IsInstanceValid(row))
                    row.QueueFree();
            }

            _spawnedRows.Clear();
            _rows.Clear();
        }

        private void DiscoverInlineRows()
        {
            var container = GetParentOrNull<Node>();
            if (container == null)
                return;

            _inlineRows.RemoveAll(row => !IsInstanceValid(row) || row.GetParent() != container);

            foreach (var child in container.GetChildren())
            {
                if (child == this || _spawnedRows.Contains(child) || _inlineRows.Contains(child))
                    continue;

                if (IsInlineRow(child))
                    _inlineRows.Add(child);
            }
        }

        private bool IsInlineRow(Node child)
        {
            if (child.HasMeta(InlineItemMetadata) && child.GetMeta(InlineItemMetadata).AsBool())
                return true;

            return _itemPrefab != null &&
                   !string.IsNullOrEmpty(child.SceneFilePath) &&
                   string.Equals(child.SceneFilePath, _itemPrefab.ResourcePath, StringComparison.Ordinal);
        }

        private void ReconcileRows()
        {
            var container = GetParentOrNull<Node>();
            if (container == null || _list == null || _listUntyped == null)
                return;

            DiscoverInlineRows();

            var desiredCount = _list.Count;
            var inlineCount = Math.Min(desiredCount, _inlineRows.Count);

            for (var i = _inlineRows.Count - 1; i >= inlineCount; i--)
                RemoveRow(_inlineRows[i], inline: true);

            var dynamicRows = _rows.Where(_spawnedRows.Contains).ToList();
            var desiredDynamicCount = desiredCount - inlineCount;

            for (var i = dynamicRows.Count - 1; i >= desiredDynamicCount; i--)
                RemoveRow(dynamicRows[i], inline: false);

            dynamicRows = _rows.Where(_spawnedRows.Contains).ToList();
            while (dynamicRows.Count < desiredDynamicCount)
            {
                var row = SpawnRow(container);
                if (row == null)
                    break;

                dynamicRows.Add(row);
            }

            _rows.Clear();
            for (var i = 0; i < inlineCount && i < _inlineRows.Count; i++)
                _rows.Add(_inlineRows[i]);
            _rows.AddRange(dynamicRows);

            for (var i = 0; i < _rows.Count; i++)
            {
                if (_listUntyped.TryGetUntyped(i, out var item))
                    SetItemData(_rows[i], item);
            }
        }

        private void ReconcileRowsDeferred()
        {
            _reconcileQueued = false;
            ReconcileRows();
        }

        private Node SpawnRow(Node container)
        {
            if (_itemPrefab == null)
                return null;

            var row = _itemPrefab.Instantiate<Node>();
            _spawnedRows.Add(row);
            container.AddChild(row);

            // Keep generated rows beside the inline rows, before this binding
            // node so the HBoxContainer preserves the intended visual order.
            container.MoveChild(row, GetIndex());

#if DEBUG
                Log.LogDebug($"[StaticCollectionBinding:{SafePath(this)}] Spawned additional row '{row.Name}' for collection '{_key}'.");
#endif
            return row;
        }

        private void RemoveRow(Node row, bool inline)
        {
            if (row == null)
                return;

            if (IsInstanceValid(row))
            {
#if DEBUG
                    Log.LogDebug($"[StaticCollectionBinding:{SafePath(this)}] Removed {(inline ? "inline" : "spawned")} row '{row.Name}' for collection '{_key}'.");
#endif
                row.QueueFree();
            }

            _inlineRows.Remove(row);
            _spawnedRows.Remove(row);
            _rows.Remove(row);
        }
    }
}
