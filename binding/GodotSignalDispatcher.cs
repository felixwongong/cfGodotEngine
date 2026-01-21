using Godot;

namespace cfGodotEngine.Binding;

/// <summary>
/// Godot implementation of ISignalDispatcher using Godot's signal system.
/// Used by default in all Binder nodes.
/// </summary>
public class GodotSignalDispatcher : ISignalDispatcher
{
    private readonly Node _node;
    private readonly string _signalName;

    public GodotSignalDispatcher(Node node, string signalName)
    {
        _node = node;
        _signalName = signalName;
    }

    public void Dispatch<T>(string signalName, T value)
    {
        if (_node.HasSignal(_signalName))
        {
            _node.EmitSignal(_signalName, Variant.From(value));
        }
    }
}
