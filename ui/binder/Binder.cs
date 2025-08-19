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
        godotObject.GetIndexed(indexPathCache);
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
        var t = propertyValue.As<T>();
        return t;
    }
    
    protected abstract void _PropertyChanged(T propertyValue);
}