using System.Collections.Generic;
using System.Text.Json;
using cfEngine;            // Log
using cfEngine.Command;    // CommandService, ICommandHandler, Parameters

namespace cfGodotEngine.Debug;

/// <summary>
/// Root `help` command: emits the discovered CommandService scope/handler list as a JSON
/// line in the captured logs (the repo's structured-output convention). Consumed by the
/// `console_list` MCP tool over the console bridge.
/// </summary>
public sealed class HelpCommand : ICommandHandler
{
    public bool Execute(Parameters param)
    {
        var commands = CommandConsoleCore.DiscoverCommands(CommandService.instance);
        Log.LogInfo($"help: {JsonSerializer.Serialize(new Dictionary<string, object> { ["commands"] = commands })}");
        return true;
    }
}
