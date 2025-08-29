using cfEngine.Util;
using cfGodotEngine.Util;
using Godot;

namespace cfGodotEngine.Binding;

[Tool]
[CustomInspector]
public abstract partial class Binder: Node
{
    [Export(PropertyHint.TypeString, "cfGodotEngine.Binding.IBindingSource")] 
    private TypeRef sourceType;
    
    [Export]
    private Node sourceNode
    {
        get => _bindingSource as Node;
        set
        {
            switch (value)
            {
                case null:
                    SetSource(_bindingSource);
                    break;
                case IBindingSource source:
                    SetSource(source);
                    break;
                default:
                    GD.PrintErr("Property source node does not implement IPropertySource. Cannot set property source.");
                    break;
            }
        }
    }

    private IBindingSource _bindingSource;

    public void SetSource(IBindingSource source)
    {
        if (_bindingSource != null)
            _bindingSource.GetBindings?.UnregisterPropertyChange(OnPropertyChanged);
        
        _bindingSource = source;
        if (_bindingSource != null)
            _bindingSource.GetBindings?.RegisterPropertyChange(OnPropertyChanged);
    }

    protected abstract void OnPropertyChanged(string propertyName, object propertyValue);
}