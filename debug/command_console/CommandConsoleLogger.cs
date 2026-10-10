using System;
using System.Collections.Generic;
using cfEngine;
using cfEngine.Command;

namespace cfGodotEngine.Debug;

public sealed class CommandConsoleLogger : ILogger
{
    private readonly ILogger _inner;
    private readonly Action<string, LogLevel> _onLog;

    public CommandConsoleLogger(ILogger inner, Action<string, LogLevel> onLog)
    {
        _inner = inner;
        _onLog = onLog;
    }

    public void LogDebug(string message, object context = null)
    {
        _inner?.LogDebug(message, context);
        _onLog(message, LogLevel.Debug);
    }

    public void LogInfo(string message, object context = null)
    {
        _inner?.LogInfo(message, context);
        _onLog(message, LogLevel.Info);
    }

    public void Asset(bool condition, object context = null)
    {
        _inner?.Asset(condition, context);
        _onLog($"Assert failed: {condition}", LogLevel.Info);
    }

    public void LogWarning(string message, object context = null)
    {
        _inner?.LogWarning(message, context);
        _onLog(message, LogLevel.Warn);
    }

    public void LogException(Exception ex, object message = null)
    {
        _inner?.LogException(ex, message as string);
        var text = message != null ? $"{message}: {ex.Message}" : ex.Message;
        _onLog(text, LogLevel.Error);
    }

    public void LogError(string message, object context = null)
    {
        _inner?.LogError(message, context);
        _onLog(message, LogLevel.Error);
    }
}
