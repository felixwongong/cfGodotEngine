using System;
using System.Diagnostics;
using cfEngine.Logging;
using Godot;

namespace cfGodotEngine.Core;

public class GodotLogger: ILogger
{
    public void LogDebug(string message, object context = null)
    {
        if (context == null)
            GD.Print(message);
        else
            GD.Print(message, context);
    }

    public void LogInfo(string message, object context = null)
    {
        if (context == null)
            GD.Print(message);
        else
            GD.Print(message, context);
    }

    public void Asset(bool condition, object context = null)
    {
        Debug.Assert(condition);
    }

    public void LogWarning(string message, object context = null)
    {
        if (context == null)
            GD.PushWarning(message);
        else
            GD.PushWarning(message, context);
    }

    public void LogException(Exception ex, object message = null)
    {
        if (message != null)
            GD.PushError($"{message}\n{ex.Message}\n{ex.StackTrace}");
        else
            GD.PushError($"{ex.Message}\n{ex.StackTrace}");
    }

    public void LogError(string message, object context = null)
    {
        GD.PushError(message);
    }
}