using System;
using cfEngine;

namespace cfGodotEngine.Debug;

/// <summary>
/// Engine-free heuristic that maps a raw log line to a <see cref="LogLevel"/> for colouring.
/// The token lists live in one place so they can be tuned against real device output.
/// </summary>
public static class LogLineClassifier
{
    // Checked first: error-ish tokens take priority over warning tokens.
    private static readonly string[] ErrorTokens =
    {
        "error",
        "exception",
        "fatal",
        "pusherror",
        "user error"
    };

    private static readonly string[] WarnTokens =
    {
        "warn"
    };

    /// <summary>
    /// Classifies a line as <see cref="LogLevel.Error"/>, <see cref="LogLevel.Warn"/> or
    /// <see cref="LogLevel.Info"/> (default). Null/empty lines are <see cref="LogLevel.Info"/>.
    /// </summary>
    public static LogLevel Classify(string line)
    {
        if (string.IsNullOrEmpty(line))
            return LogLevel.Info;

        foreach (var token in ErrorTokens)
        {
            if (line.Contains(token, StringComparison.OrdinalIgnoreCase))
                return LogLevel.Error;
        }

        foreach (var token in WarnTokens)
        {
            if (line.Contains(token, StringComparison.OrdinalIgnoreCase))
                return LogLevel.Warn;
        }

        return LogLevel.Info;
    }
}
