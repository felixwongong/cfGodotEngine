using Godot;

namespace cfGodotEngine.Binding;

[Tool]
[GlobalClass]
public partial class TextureBinder : SinglePropertyBinder<Texture2D>
{
    [Signal] public delegate void OnValueChangedEventHandler(Texture2D value);

    protected override string GetSignalName() => SignalName.OnValueChanged;

    protected override bool ValidateValue(object value)
    {
        return value is Texture2D;
    }

    protected override Texture2D ParseValue(Variant propertyValue)
    {
        return propertyValue.As<Texture2D>();
    }
}
