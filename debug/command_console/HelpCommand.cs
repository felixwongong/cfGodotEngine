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
        Log.LogInfo($"help: {JsonSerializer.Serialize(BuildPayload(CommandService.instance))}");
        return true;
    }

    /// <summary>
    /// Builds the `help` payload. <c>commands</c> is the unchanged plain list of full command
    /// paths (a missing key must not break older consumers). <c>availability</c> additively tags
    /// each command with its wire availability string (<c>editor</c>|<c>runtime</c>|<c>both</c>);
    /// unknown future flag values fall back to the lowercased enum name. Tags rather than filters:
    /// <c>console_list</c> is cross-context discovery, so it must not lie about what exists.
    /// </summary>
    public static Dictionary<string, object> BuildPayload(CommandService service)
    {
        var commands = CommandConsoleCore.DiscoverCommands(service);

        var availability = new Dictionary<string, string>();
        foreach (var pair in CommandConsoleCore.DiscoverAvailability(service))
            availability[pair.Key] = AvailabilityWire(pair.Value);

        return new Dictionary<string, object>
        {
            ["commands"] = commands,
            ["availability"] = availability,
        };
    }

    private static string AvailabilityWire(CommandAvailability value)
    {
        switch (value)
        {
            case CommandAvailability.Editor:
                return "editor";
            case CommandAvailability.Runtime:
                return "runtime";
            case CommandAvailability.Both:
                return "both";
            default:
                return value.ToString().ToLowerInvariant();
        }
    }
}
