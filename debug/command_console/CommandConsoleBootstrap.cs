using Godot;

namespace cfGodotEngine.Debug;

/// <summary>
/// Autoload bootstrap that instantiates the in-game command console overlay.
/// Always compiled, including release APKs.
/// </summary>
public partial class CommandConsoleBootstrap : Node
{
    public override void _Ready()
    {
        AddChild(new CommandConsoleOverlay());
#if TOOLS
        AddChild(new CommandConsoleBridgeHost());
#endif
        GD.Print("[CommandConsole] Ready; tap LOG or press F12.");
    }
}
