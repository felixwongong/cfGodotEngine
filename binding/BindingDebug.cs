using System.Diagnostics;
using cfEngine;
using Godot;

namespace cfGodotEngine.Binding
{
    /// <summary>
    /// Central toggle for verbose binding pipeline logging. Available in Debug
    /// builds only. Set the <c>CATSWEEPER_BINDING_VERBOSE=1</c> environment
    /// variable before launching Godot to enable per-apply traces.
    /// Silent-failure warnings are always on and do not depend on this flag.
    /// </summary>
    public static class BindingDebug
    {
        static BindingDebug()
        {
        }

        [Conditional("DEBUG")]
        public static void LogVerbose(string message)
        {
            Log.LogDebug(message);
        }
    }
}