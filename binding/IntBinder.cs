using System.Globalization;
using Godot;

namespace cfGodotEngine.Binding;

[Tool]
[GlobalClass]
public partial class IntBinder : SinglePropertyBinder<int>
{
    [Signal] public delegate void OnValueChangedEventHandler(int value);
    [Signal] public delegate void OnValueTextChangedEventHandler(string text);
    
    protected override string GetSignalName() => SignalName.OnValueChanged;
    
    protected override bool ValidateValue(object value)
    {
        return base.ValidateValue(value) && value is int;
    }

    protected override int ParseValue(object propertyValue)
    {
        return propertyValue is int intValue ? intValue : 0;
    }
    
    protected override void OnPropertyChanged(int value)
    {
        base.OnPropertyChanged(value);
        
        // Emit secondary text signal if connected
        if(HasConnections(SignalName.OnValueTextChanged))
            EmitSignalOnValueTextChanged(value.ToString(CultureInfo.InvariantCulture));
    }
}