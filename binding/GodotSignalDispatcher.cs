using Godot;

namespace cfGodotEngine.Binding;

/// <summary>
/// Godot implementation of ISignalDispatcher using Godot's signal system.
/// Used by default in all Binder nodes.
/// </summary>
public class GodotSignalDispatcher : ISignalDispatcher
{
    private readonly Node _node;

    public GodotSignalDispatcher(Node node)
    {
        _node = node;
    }

    public void Dispatch<T>(string signalName, T value)
    {
        if (_node.HasSignal(signalName))
        {
            _node.EmitSignal(signalName, Variant.From(value));
        }
    }
}
