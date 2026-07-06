using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using cfEngine.DataStructure;
using Godot;

namespace cfGodotEngine.Binding;

[Tool]
[CustomInspector]
public abstract partial class Binder : Node
{
    [Export(PropertyHint.None, "BindingSourceType")]
    private StringName _sourceType;

    [Export(PropertyHint.None, "BindingSourceKey")]
    private StringName _sourceKey;

    [Export]
    private NodePath _sourceOverride;

    [Export]
    private NodePath _mockSource;

    [Export]
    private NodePath _targetNode;

    [Export(PropertyHint.None, "BindingTargetProperty")]
    private StringName _targetProperty;

    [Export]
    private BindingConverter[] _converters;

    private IBindingSource _bindingSource;

    protected IBindingSource BindingSource => _bindingSource;

    public StringName SourceType => _sourceType;
    public StringName SourceKey => _sourceKey;
    public NodePath TargetNode => _targetNode;
    public StringName TargetProperty => _targetProperty;
    public BindingConverter[] Converters => _converters;

    public override void _Ready()
    {
        base._Ready();
        ResolveBindingSource();
    }

    public override void _EnterTree()
    {
        base._EnterTree();
        if (Engine.IsEditorHint())
            ResolveBindingSource();
    }

    public override void _ExitTree()
    {
        UnsubscribeFromSource();
        base._ExitTree();
    }

    private void ResolveBindingSource()
    {
        UnsubscribeFromSource();
        _bindingSource = FindBindingSource();
        if (_bindingSource != null)
        {
            OnBindingRestored(_bindingSource.GetBindings);
            _bindingSource.GetBindings?.RegisterPropertyChange(OnBindingValueChanged);
        }
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

    private void UnsubscribeFromSource()
    {
        if (_bindingSource != null)
        {
            _bindingSource.GetBindings?.UnregisterPropertyChange(OnBindingValueChanged);
            _bindingSource = null;
        }
    }

    protected abstract void OnBindingRestored(IPropertyMap bindingMap);
    protected abstract void OnBindingValueChanged(string propertyName);

    protected Node GetTargetNode()
    {
        if (_targetNode == null || _targetNode.IsEmpty)
            return null;
        return GetNodeOrNull<Node>(_targetNode);
    }

    protected Variant ApplyConverters(Variant input)
    {
        if (_converters == null)
            return input;

        var current = input;
        foreach (var converter in _converters)
        {
            if (converter == null)
                continue;
            current = converter.Convert(current);
        }
        return current;
    }

    protected void ApplyToTarget(Variant value)
    {
        var target = GetTargetNode();
        if (target == null || _targetProperty == null || _targetProperty.IsEmpty)
            return;
        target.Set(_targetProperty, value);
    }

    protected void DispatchSignal(StringName signalName, Variant value)
    {
        if (HasSignal(signalName))
            EmitSignal(signalName, value);
    }

    public virtual string ValidateBinding()
    {
        if (string.IsNullOrWhiteSpace(_sourceKey))
            return "Source key is not set";

        if (_sourceOverride != null && !_sourceOverride.IsEmpty)
        {
            var node = GetNodeOrNull<Node>(_sourceOverride);
            if (node == null)
                return $"Source override '{_sourceOverride}' not found";
            if (node is not IBindingSource)
                return $"Source override '{_sourceOverride}' is not an IBindingSource";
        }

        var source = FindBindingSource();
        if (source != null)
        {
            var keys = GetBindingKeysFor(source);
            if (keys != null && keys.Contains(_sourceKey.ToString()))
                return $"Source key '{_sourceKey}' is not exposed by source '{source.GetType().Name}'";
        }

        var target = GetTargetNode();
        if (target == null && _targetNode != null && !_targetNode.IsEmpty)
            return $"Target node '{_targetNode}' not found";

        if (target != null && (_targetProperty == null || _targetProperty.IsEmpty))
            return "Target property is not set";

        return string.Empty;
    }

    protected static IReadOnlyList<string> GetBindingKeysFor(IBindingSource source)
    {
        var method = source.GetType().GetMethod("GetBindingKeys",
            BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
        if (method == null)
            return null;
        return method.Invoke(null, null) as IReadOnlyList<string>;
    }
}
