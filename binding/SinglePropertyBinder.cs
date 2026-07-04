using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using cfEngine;
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
    public ISignalDispatcher SignalDispatcher => _signalDispatcher ??= CreateSignalDispatcher();
    
    /// <summary>
    /// Creates the signal dispatcher. Override to provide custom implementation (e.g., for testing).
    /// </summary>
    protected virtual ISignalDispatcher CreateSignalDispatcher()
    {
        return new GodotSignalDispatcher(this);
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

        if (bindingMap.Get<T>(_bindingName.propertyName, out var propertyValue) && !ValidateValue(propertyValue))
        {
            GD.PrintErr("SinglePropertyBinder: Property value is null or invalid. Cannot parse value.");
            return;
        }
        
        var parsed = ParseValue(propertyValue);
        OnPropertyChanged(parsed);
        
        // Use abstracted signal dispatcher instead of direct Godot signal
        SignalDispatcher?.Dispatch(GetSignalName(), parsed);
    }
    
    protected override void OnBindingValueChanged(string propertyName)
    {
        if(!propertyName.Equals(_bindingName?.propertyName))
            return;

        // Retrieve value from cached binding source
        var source = BindingSource;
        if (source == null)
        {
            GD.PrintErr("SinglePropertyBinder: Could not find IBindingSource.");
            return;
        }

        if (!source.GetBindings.Get<T>(propertyName, out var propertyValue))
        {
            GD.PrintErr("SinglePropertyBinder: Could not retrieve property value.");
            return;
        }

        if (!ValidateValue(propertyValue))
        {
            GD.PrintErr("SinglePropertyBinder: Property value is null or invalid. Cannot parse value.");
            return;
        }
        
        var parsed = ParseValue(propertyValue);
        OnPropertyChanged(parsed);
        
        // Use abstracted signal dispatcher instead of direct Godot signal
        SignalDispatcher?.Dispatch(GetSignalName(), parsed);
    }
    
    protected virtual bool ValidateValue(object value)
    {
        return value != null;
    }
    
    protected abstract T ParseValue(object propertyValue);

    protected virtual void OnPropertyChanged(T value)
    {
    }

    /// <summary>
    /// Validates that the configured binding source type exists and exposes the configured property key.
    /// </summary>
    public override string ValidateBinding()
    {
        if (_bindingName == null)
            return "BindingName is not assigned";

        if (string.IsNullOrWhiteSpace(_bindingName.typeName))
            return string.Empty; // not configured yet, don't spam

        if (string.IsNullOrWhiteSpace(_bindingName.propertyName))
            return "Binding property name is empty";

        var sourceType = System.Type.GetType(_bindingName.typeName);
        if (sourceType == null)
            return $"Source type '{_bindingName.typeName}' could not be loaded";

        var method = sourceType.GetMethod("GetBindingKeys", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
        if (method == null)
            return $"Source type '{sourceType.Name}' does not expose GetBindingKeys()";

        var keys = method.Invoke(null, null) as IReadOnlyList<string>;
        if (keys == null || !keys.Contains(_bindingName.propertyName))
            return $"Property '{_bindingName.propertyName}' is not a valid binding key on '{sourceType.Name}'";

        return string.Empty;
    }
}
