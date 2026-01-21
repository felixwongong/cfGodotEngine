using cfEngine.DataStructure;
using cfGodotEngine.Util;
using Godot;

namespace cfGodotEngine.Binding;

/// <summary>
/// Binder for binding to a <see cref="IPropertyMap"/> using <see cref="_bindingName"/>
/// When property updated, signal will be dispatched via <see cref="ISignalDispatcher"/>.
/// For inherited member,
/// 1. override the <see cref="OnPropertyChanged"/> to add custom handling for the <typeparam name="T"/> value
/// 2. override the <see cref="ParseValue"/> for adding parsing from object to <typeparam name="T"/>
/// 3. override the <see cref="ValidateValue"/> to add custom validation for the object value
/// </summary>
public abstract partial class SinglePropertyBinder<T> : Binder
{
    [Export] private BindingName _bindingName;
    
    private ISignalDispatcher _signalDispatcher;
    
    public override void _Ready()
    {
        base._Ready();
        
        // Initialize Godot signal dispatcher
        _signalDispatcher = CreateSignalDispatcher();
    }
    
    /// <summary>
    /// Creates the signal dispatcher. Override to provide custom implementation (e.g., for testing).
    /// </summary>
    protected virtual ISignalDispatcher CreateSignalDispatcher()
    {
        return new GodotSignalDispatcher(this, GetSignalName());
    }
    
    /// <summary>
    /// Returns the signal name used for dispatching. Override in derived classes.
    /// </summary>
    protected abstract string GetSignalName();
    
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
        
        // Use abstracted signal dispatcher instead of direct Godot signal
        _signalDispatcher?.Dispatch(GetSignalName(), parsed);
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
        
        // Use abstracted signal dispatcher instead of direct Godot signal
        _signalDispatcher?.Dispatch(GetSignalName(), parsed);
    }
    
    protected virtual bool ValidateValue(object value)
    {
        return value != null;
    }
    
    protected abstract T ParseValue(object propertyValue);

    protected virtual void OnPropertyChanged(T value)
    {
    }
}