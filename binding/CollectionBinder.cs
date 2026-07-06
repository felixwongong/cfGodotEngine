using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using cfEngine.DataStructure;
using Godot;

namespace cfGodotEngine.Binding;

/// <summary>
/// Binds an <see cref="System.Collections.IEnumerable"/> source key to a container, instantiating
/// one <see cref="_itemPrefab"/> per element and passing the element to a setter
/// method on each instantiated item.
/// </summary>
[Tool]
[GlobalClass]
public partial class CollectionBinder : Binder
{
    [Export]
    private NodePath _container;

    [Export]
    private PackedScene _itemPrefab;

    [Export]
    private StringName _itemSetterMethod = "SetData";

    private readonly List<object> _currentItems = new();

    protected override void OnBindingRestored(IPropertyMap bindingMap)
    {
        Refresh(bindingMap);
    }

    protected override void OnBindingValueChanged(string propertyName)
    {
        if (propertyName != SourceKey)
            return;

        Refresh(BindingSource?.GetBindings);
    }

    private void Refresh(IPropertyMap bindingMap)
    {
        if (bindingMap == null)
            return;

        if (!bindingMap.Get<global::System.Collections.IEnumerable>(SourceKey, out var collection) || collection == null)
            return;

        var newItems = collection.Cast<object>().ToList();
        if (SequencesEqual(_currentItems, newItems))
            return;

        ClearItems();

        var container = GetContainer();
        if (container == null || _itemPrefab == null)
            return;

        foreach (var item in newItems)
        {
            var node = _itemPrefab.Instantiate<Node>();
            container.AddChild(node);
            SetItemData(node, item);
        }

        _currentItems.Clear();
        _currentItems.AddRange(newItems);
    }

    private void ClearItems()
    {
        var container = GetContainer();
        if (container == null)
            return;

        foreach (var child in container.GetChildren())
        {
            child.QueueFree();
        }

        _currentItems.Clear();
    }

    private Node GetContainer()
    {
        if (_container == null || _container.IsEmpty)
            return this;

        return GetNodeOrNull<Node>(_container);
    }

    private void SetItemData(Node item, object data)
    {
        if (item == null || _itemSetterMethod == null || _itemSetterMethod.IsEmpty)
            return;

        var method = item.GetType().GetMethod(_itemSetterMethod,
            BindingFlags.Public | BindingFlags.Instance,
            null, new[] { typeof(object) }, null);

        if (method != null)
        {
            method.Invoke(item, new[] { data });
            return;
        }

        var variantMethod = item.GetType().GetMethod(_itemSetterMethod,
            BindingFlags.Public | BindingFlags.Instance,
            null, new[] { typeof(Variant) }, null);

        if (variantMethod != null)
            variantMethod.Invoke(item, new object[] { Variant.From(data) });
    }

    public override string ValidateBinding()
    {
        if (string.IsNullOrWhiteSpace(SourceKey))
            return "Source key is not set";

        var source = BindingSource;
        if (source != null)
        {
            var keys = GetBindingKeysFor(source);
            if (keys != null && !keys.Contains(SourceKey.ToString()))
                return $"Source key '{SourceKey}' is not exposed by source '{source.GetType().Name}'";
        }

        if (_itemPrefab == null)
            return "Item prefab is not assigned";

        var container = GetContainer();
        if (container == null)
            return "Container node not found";

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
