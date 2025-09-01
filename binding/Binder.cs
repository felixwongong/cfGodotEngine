using cfEngine.DataStructure;
using cfGodotEngine.Util;
using Godot;

namespace cfGodotEngine.Binding;

[Tool]
[CustomInspector]
public abstract partial class Binder: Node
{
    private IBindingSource _bindingSource;

    public override void _Ready()
    {
        base._Ready();

        for (var x = GetParentOrNull<Node>(); x != null; x = x.GetParentOrNull<Node>())
        {
            if (x is not IBindingSource source)
                continue;
            
            SetSource(source);
            break;
        }

        if (_bindingSource == null)
            GD.PrintErr("Binder: No IBindingSource found in parents.");
    }

    private void SetSource(IBindingSource source)
    {
        if (_bindingSource != null)
            _bindingSource.GetBindings?.UnregisterPropertyChange(OnBindingValueChanged);
        
        _bindingSource = source;
        OnBindingRestored(_bindingSource.GetBindings);
        if (_bindingSource != null)
            _bindingSource.GetBindings?.RegisterPropertyChange(OnBindingValueChanged);
    }

    protected abstract void OnBindingRestored(IPropertyMap bindingMap);
    protected abstract void OnBindingValueChanged(string propertyName, object propertyValue);
}