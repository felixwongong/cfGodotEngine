using System;
using cfEngine.DataStructure;
using Godot;

namespace cfGodotEngine.Binding;

/// <summary>
/// Typed binder that reads a value of type <typeparamref name="T"/> from the binding source,
/// runs it through converters, applies it directly to the configured target property,
/// and optionally emits an OnValueChanged signal with the raw source value.
/// </summary>
public abstract partial class SinglePropertyBinder<T> : Binder
{
    protected abstract string GetSignalName();

    protected override void OnBindingRestored(IPropertyMap bindingMap)
    {
        if (bindingMap == null)
        {
            GD.PrintErr($"{GetType().Name}: Binding map is null.");
            return;
        }

        if (!bindingMap.Get<T>(SourceKey, out var propertyValue) || !ValidateValue(propertyValue))
        {
            GD.PrintErr($"{GetType().Name}: Could not read or validate source key '{SourceKey}'.");
            return;
        }

        var rawVariant = Variant.From(propertyValue);
        var convertedVariant = ApplyConverters(rawVariant);

        OnPropertyChanged(ParseValue(rawVariant));
        ApplyToTarget(convertedVariant);
        DispatchSignal(GetSignalName(), rawVariant);
    }

    protected override void OnBindingValueChanged(string propertyName)
    {
        if (propertyName != SourceKey)
            return;

        OnBindingRestored(BindingSource?.GetBindings);
    }

    protected virtual bool ValidateValue(object value)
    {
        return value != null;
    }

    protected abstract T ParseValue(Variant propertyValue);

    protected virtual void OnPropertyChanged(T value)
    {
    }
}
