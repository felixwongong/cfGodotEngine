using cfEngine.DataStructure;
using cfGodotEngine.Util;
using Godot;

namespace cfGodotEngine.Binding;

/// <summary>
/// Binder for binding to a <see cref="IPropertyMap"/> using <see cref="_bindingName"/>
/// When property updated, signal <see cref="OnPropertyChanged"/> will be emitted, and <see cref="OnBindingValueChanged"/> will be called.
/// For inherited member,
/// 1. override the <see cref="OnBindingValueChanged"/> to add custom handling for the <typeparam name="T"/> value
/// 2. override the <see cref="ParseValue"/> for adding parsing from object to <typeparam name="T"/>
/// 3. override the <see cref="ValidateValue"/> to add custom validation for the object value
/// </summary>
public abstract partial class SinglePropertyBinder<T> : Binder
{
    [Export] private BindingName _bindingName;
    
    protected override void OnBindingRestored(IPropertyMap bindingMap)
    {
        if (bindingMap == null)
        {
            GD.PrintErr("SinglePropertyBinder: Binding map is null.");
            return;
        }

        bindingMap.Get(_bindingName.propertyName, out object propertyValue);
        if (!ValidateValue(propertyValue))
        {
            GD.PrintErr("SinglePropertyBinder: Property value is null or invalid. Cannot parse value.");
            return;
        }
        
        var parsed = ParseValue(propertyValue);
        OnPropertyChanged(parsed);
        DispatchSignal(parsed);
    }
    
    protected override void OnBindingValueChanged(string propertyName, object propertyValue)
    {
        if(!propertyName.Equals(_bindingName?.propertyName))
            return;

        if (!ValidateValue(propertyValue))
        {
            GD.PrintErr("SinglePropertyBinder: Property value is null or invalid. Cannot parse value.");
            return;
        }
        
        var parsed = ParseValue(propertyValue);
        OnPropertyChanged(parsed);
        DispatchSignal(parsed);
    }
    
    protected virtual bool ValidateValue(object value)
    {
        return value != null;
    }
    
    protected abstract T ParseValue(object propertyValue);
    
    protected abstract void DispatchSignal(T value);

    protected virtual void OnPropertyChanged(T value)
    {
    }
}