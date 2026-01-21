using Godot;

namespace cfGodotEngine.Binding;

[Tool]
[GlobalClass]
public partial class StringBinder: SinglePropertyBinder<string>
{
    [Signal] public delegate void OnValueChangedEventHandler(string value);
    
    protected override string GetSignalName() => SignalName.OnValueChanged;
    
    protected override string ParseValue(object propertyValue)
    {
        if (!ValidateValue(propertyValue))
        {
            GD.PrintErr("StringBinder: Property value is null. Cannot parse value.");
            return string.Empty;
        }

        return propertyValue.ToString();
    }
}