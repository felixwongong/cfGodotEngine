using Godot;

namespace cfGodotEngine.UI;

public abstract partial class Binder<[MustBeVariant] T>: Node
{
    [Export] private string propertyName;
    [Export] private string indexPath;

    private NodePath indexPathCache; 

    public void OnPropertyChanged(string propertyName, GodotObject godotObject)
    {
        if(!propertyName.Equals(this.propertyName)) 
            return;

        indexPathCache ??= indexPath;

        var indexedProperty = godotObject.GetIndexed(indexPathCache);
        if (indexedProperty.VariantType == Variant.Type.Nil)
        {
            GD.PrintErr($"Property '{propertyName}' not found on object {godotObject} at index path '{indexPath}'");
            return;
        }
        
        OnPropertyChanged(propertyName, indexedProperty);
    }
    
    public void OnPropertyChanged(string propertyName, Variant propertyValue)
    {
        if(!propertyName.Equals(this.propertyName)) 
            return;
        
        var t = ValidatePropertyValue(propertyValue);
        if (t == null)
        {
            GD.PrintErr($"Invalid property value for {propertyName}: {propertyValue}");
            return;
        }
        
        _PropertyChanged(t);
    }

    protected virtual T ValidatePropertyValue(Variant propertyValue)
    {
        if(propertyValue.VariantType == Variant.Type.Nil)
        {
            GD.PrintErr($"Property '{propertyName}' is nil on object {this}");
            return default;
        }

        var t = propertyValue.As<T>();
        return t;
    }
    
    protected abstract void _PropertyChanged(T propertyValue);
}