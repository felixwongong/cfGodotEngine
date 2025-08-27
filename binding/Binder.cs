using cfEngine.DataStructure;
using Godot;
using Godot.Collections;

namespace cfGodotEngine.Binding;

[Tool]
public abstract partial class Binder: Node
{
    [Export]
    private Node propertySourceNode
    {
        get => _propertySource as Node;
        set
        {
            switch (value)
            {
                case null:
                    _propertySource = null;
                    break;
                case IPropertySource source:
                    _propertySource = source;
                    SetProperties(source.GetProperties);
                    break;
                default:
                    GD.PrintErr("Property source node does not implement IPropertySource. Cannot set property source.");
                    break;
            }
        }
    }

    private IPropertySource _propertySource;

    public void SetProperties(IPropertyMap properties)
    {
        if (properties == null)
        {
            GD.PrintErr("Property map is null. Cannot set property map.");
            return;
        }

        properties.RegisterPropertyChange(OnPropertyChanged);
    }

    protected abstract void OnPropertyChanged(string propertyName, object propertyValue);
}