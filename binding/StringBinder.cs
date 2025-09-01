using cfEngine.DataStructure;
using cfGodotEngine.Util;
using Godot;

namespace cfGodotEngine.Binding;

/// <summary>
/// Binder for binding to a <see cref="IPropertyMap"/> using <see cref="_bindingName"/>
/// When property updated, signal <see cref="OnValueChanged"/> will be emitted, and <see cref="OnBindingValueChanged"/> will be called.
/// For inherited member,
/// 1. override the <see cref="OnBindingValueChanged"/> to add custom handling for the string value
/// 2. override the <see cref="ParseValue"/> for adding parsing from object to string
/// 3. override the <see cref="ValidateValue"/> to add custom validation for the object value
/// </summary>

[Tool]
[GlobalClass]
public partial class StringBinder: Binder
{
    [Signal] public delegate void OnValueChangedEventHandler(string value);

    [Export] private BindingName _bindingName;

    protected override void OnBindingRestored(IPropertyMap bindingMap)
    {
        if (bindingMap == null)
        {
            GD.PrintErr("StringBinder: Binding map is null.");
            return;
        }

        bindingMap.Get(_bindingName.propertyName, out object propertyValue);
        if (propertyValue != null)
        {
            var parsed = ParseValue(propertyValue);
            OnPropertyChanged(parsed);
            EmitSignalOnValueChanged(parsed);
        }
    }

    protected override void OnBindingValueChanged(string propertyName, object propertyValue)
    {
        if(!propertyName.Equals(_bindingName?.propertyName))
            return;
        
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