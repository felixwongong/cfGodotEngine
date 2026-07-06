using Godot;
using System;

namespace cfGodotEngine.Binding;

/// <summary>
/// Base class for value converters in the binding pipeline.
/// Converters transform a source <see cref="Variant"/> into another <see cref="Variant"/>
/// before it is applied to the target property.
/// </summary>
[GlobalClass]
public abstract partial class BindingConverter : Resource
{
    /// <summary>
    /// Converts the input value to the target representation.
    /// </summary>
    public abstract Variant Convert(Variant input);

    /// <summary>
    /// Converts the value back to the source representation, if reversible.
    /// Override only when two-way binding is meaningful for this converter.
    /// </summary>
    public virtual Variant ConvertBack(Variant output)
    {
        throw new NotSupportedException($"{GetType().Name} does not support two-way conversion.");
    }
}
