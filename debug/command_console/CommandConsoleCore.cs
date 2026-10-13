using System;
using System.Collections.Generic;
using cfEngine;
using cfEngine.Command;

namespace cfGodotEngine.Debug;

public static class CommandConsoleCore
{
    public static IReadOnlyList<string> DiscoverCommands(CommandService service)
    {
        var commands = new List<string>();
        DiscoverRecursive(service, string.Empty, commands);
        return commands;
    }

    private static void DiscoverRecursive(CommandService service, string prefix, List<string> commands)
    {
        foreach (var handlerName in service.GetHandlerNames())
        {
            var full = string.IsNullOrEmpty(prefix) ? handlerName : $"{prefix} {handlerName}";
            commands.Add(full);
        }

        foreach (var scopeName in service.GetScopeNames())
        {
            var nextPrefix = string.IsNullOrEmpty(prefix) ? scopeName : $"{prefix} {scopeName}";
            if (service.TryGetScope(scopeName, out var subService))
            {
                DiscoverRecursive(subService!, nextPrefix, commands);
            }
        }
    }

    /// <summary>
    /// Flattens the same scope/handler walk as <see cref="DiscoverCommands"/> but returns each
    /// command's declared <see cref="CommandAvailability"/> keyed by full space-joined path.
    /// Handlers that fail <c>TryGetHandler</c> (or resolve to null) are skipped; the walk order
    /// matches <see cref="DiscoverCommands"/>. Engine-free and non-executing.
    /// </summary>
    public static IReadOnlyDictionary<string, CommandAvailability> DiscoverAvailability(CommandService service)
    {
        var availability = new Dictionary<string, CommandAvailability>();
        DiscoverAvailabilityRecursive(service, string.Empty, availability);
        return availability;
    }

    private static void DiscoverAvailabilityRecursive(
        CommandService service, string prefix, Dictionary<string, CommandAvailability> availability)
    {
        foreach (var handlerName in service.GetHandlerNames())
        {
            var full = string.IsNullOrEmpty(prefix) ? handlerName : $"{prefix} {handlerName}";
            if (service.TryGetHandler(handlerName, out var handler) && handler != null)
                availability[full] = CommandService.ReadAvailability(handler);
        }

        foreach (var scopeName in service.GetScopeNames())
        {
            var nextPrefix = string.IsNullOrEmpty(prefix) ? scopeName : $"{prefix} {scopeName}";
            if (service.TryGetScope(scopeName, out var subService))
            {
                DiscoverAvailabilityRecursive(subService!, nextPrefix, availability);
            }
        }
    }

    public static IReadOnlyList<string> GetCompletions(string partial, IReadOnlyList<string> allCommands)
    {
        if (string.IsNullOrWhiteSpace(partial))
            return allCommands;

        var results = new List<string>();
        foreach (var cmd in allCommands)
        {
            if (cmd.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
                results.Add(cmd);
        }
        return results;
    }

    public static bool ExecuteAndCapture(
        CommandService service,
        string input,
        Action<string, LogLevel> onLog,
        Action<CommandExecutionEvent> onEvent)
    {
        var originalLogger = Log.Logger;
        var decorator = new CommandConsoleLogger(originalLogger, onLog);
        Log.SetLogger(decorator);

        var success = false;
        void Handler(CommandExecutionEvent evt)
        {
            success = evt.Success;
            onEvent(evt);
        }
        service.CommandExecuted += Handler;

        try
        {
            service.Execute(input);
        }
        finally
        {
            service.CommandExecuted -= Handler;
            Log.SetLogger(originalLogger);
        }

        return success;
    }
}

public sealed class CommandHistory
{
    private readonly List<string> _entries = new();
    private int _cursor = -1;

    public int Count => _entries.Count;

    public void Push(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return;

        _entries.Add(command);
        _cursor = _entries.Count;
    }

    public string Previous()
    {
        if (_entries.Count == 0)
            return null;

        _cursor = Math.Max(0, _cursor - 1);
        return _entries[_cursor];
    }

    public string Next()
    {
        if (_entries.Count == 0)
            return null;

        _cursor = Math.Min(_entries.Count, _cursor + 1);
        return _cursor < _entries.Count ? _entries[_cursor] : string.Empty;
    }

    public void Clear()
    {
        _entries.Clear();
        _cursor = -1;
    }
}
