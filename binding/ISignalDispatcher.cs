namespace cfGodotEngine.Binding;

/// <summary>
/// Abstraction for signal dispatching, allowing binding system to work without direct Godot dependencies.
/// Enables unit testing and cross-platform reuse.
/// </summary>
public interface ISignalDispatcher
{
    /// <summary>
    /// Dispatches a signal with the given name and value.
    /// </summary>
    void Dispatch<T>(string signalName, T value);
}
