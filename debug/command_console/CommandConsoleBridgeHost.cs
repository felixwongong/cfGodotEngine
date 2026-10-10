#if TOOLS

using Godot;

namespace cfGodotEngine.Debug;

/// <summary>
/// Editor-only game-side host: instantiates a <see cref="ConsoleBridgeServer"/> with
/// <c>Context="runtime"</c> so a played-from-editor game process advertises its own
/// console bridge (a second discovery file alongside the editor's). Whole-file
/// <c>#if TOOLS</c>: export builds ship nothing (D3).
/// </summary>
public partial class CommandConsoleBridgeHost : Node
{
    private ConsoleBridgeServer _server;

    public override void _Ready()
    {
        _server = new ConsoleBridgeServer { Context = "runtime" };
        AddChild(_server);
    }

    public override void _ExitTree()
    {
        if (_server != null)
        {
            _server.QueueFree();
            _server = null;
        }
    }
}

#endif
