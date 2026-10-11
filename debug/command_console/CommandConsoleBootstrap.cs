using Godot;

namespace cfGodotEngine.Debug;

/// <summary>
/// Autoload bootstrap that instantiates the in-game command console overlay and the
/// generic debug window host it shares a build with. Always compiled, including release APKs.
/// </summary>
public partial class CommandConsoleBootstrap : Node
{
    public override void _Ready()
    {
        AddChild(new DebugWindowHost());
        AddChild(new CommandConsoleOverlay());
#if TOOLS
        AddChild(new CommandConsoleBridgeHost());
#endif
        GD.Print("[CommandConsole] Ready; tap DEBUG or press F12.");
    }
}
