using cfEngine.DataStructure;
using Godot;

namespace cfGodotEngine.Binding;

public abstract partial class Binder: Node
{
    [Export] private string propertyName;
    
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