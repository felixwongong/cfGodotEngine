using cfGodotEngine.Util;
using Godot;

namespace cfGodotEngine.Binding;

[Tool]
[CustomInspector]
public abstract partial class Binder: Node
{
    [Export(PropertyHint.TypeString, "cfGodotEngine.Binding.IBindingSource")] 
    private TypeRef sourceType;
    
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
            _bindingSource.GetBindings?.UnregisterPropertyChange(OnPropertyChanged);
        
        _bindingSource = source;
        if (_bindingSource != null)
            _bindingSource.GetBindings?.RegisterPropertyChange(OnPropertyChanged);
    }

    protected abstract void OnPropertyChanged(string propertyName, object propertyValue);
}