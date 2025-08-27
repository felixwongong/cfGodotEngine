using cfEngine.DataStructure;
using Godot;

namespace cfGodotEngine.Binding;

/// <summary>
/// Binder for binding to a <see cref="IPropertyMap"/> using <see cref="propertyName"/>
/// When property updated, signal <see cref="OnValueChanged"/> will be emitted, and <see cref="OnPropertyChanged(string,object)"/> will be called.
/// For inherited member,
/// 1. override the <see cref="OnPropertyChanged(string,object)"/> to add custom handling for the string value
/// 2. override the <see cref="ParseValue"/> for adding parsing from object to string
/// 3. override the <see cref="ValidateValue"/> to add custom validation for the object value
/// </summary>

[Tool]
[GlobalClass]
public partial class StringBinder: Binder
{
    [Signal] public delegate void OnValueChangedEventHandler(string value);

    [Export] private string propertyName;

    protected override void OnPropertyChanged(string propertyName, object propertyValue)
    {
        var parsed = ParseValue(propertyValue);
        OnPropertyChanged(parsed);
        EmitSignalOnValueChanged(parsed);
    }

    protected virtual string ParseValue(object propertyValue)
    {
        if (!ValidateValue(propertyValue))
        {
            GD.PrintErr("StringBinder: Property value is null. Cannot parse value.");
            return string.Empty;
        }

        return propertyValue.ToString();
    }
    
    protected virtual bool ValidateValue(object value)
    {
        return value != null;
    }

    protected virtual void OnPropertyChanged(string value)
    {
    }
}