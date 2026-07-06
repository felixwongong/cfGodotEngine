using Godot;

namespace cfGodotEngine.Binding;

[Tool]
[GlobalClass]
public partial class FloatBinder : SinglePropertyBinder<float>
{
    [Signal] public delegate void OnValueChangedEventHandler(float value);

    protected override string GetSignalName() => SignalName.OnValueChanged;

    protected override bool ValidateValue(object value)
    {
        return value is float;
    }

    protected override float ParseValue(Variant propertyValue)
    {
        return propertyValue.VariantType == Variant.Type.Float
            ? propertyValue.AsSingle()
            : (float)propertyValue.AsDouble();
    }
}
