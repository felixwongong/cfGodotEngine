using Godot;

namespace cfGodotEngine.Binding;

[Tool]
[GlobalClass]
public partial class BoolBinder : SinglePropertyBinder<bool>
{
    [Signal] public delegate void OnValueChangedEventHandler(bool value);
    [Signal] public delegate void OnValueTextChangedEventHandler(string text);
    
    protected override string GetSignalName() => SignalName.OnValueChanged;
    
    protected override bool ValidateValue(object value)
    {
        return base.ValidateValue(value) && value is bool;
    }

    protected override bool ParseValue(object propertyValue)
    {
        return propertyValue is bool boolValue && boolValue;
    }
    
    protected override void OnPropertyChanged(bool value)
    {
        base.OnPropertyChanged(value);
        
        // Emit secondary text signal if connected
        if(HasConnections(SignalName.OnValueTextChanged))
            EmitSignalOnValueTextChanged(value.ToString());
    }
}
