using cfEngine;
using Godot;

namespace cfGodotEngine.SceneManagement;

/// <summary>
/// Shared handling for the editor-only <c>_layoutSnapshot</c> property arriving at
/// <c>_Set</c> in a build where the <c>[Export]</c> that registers it is absent
/// (export builds strip it; a broken/renamed editor partial leaves it unregistered).
/// Replaces the duplicated <c>_Set</c>/<c>LogSnapshotDiscard</c> in
/// <see cref="Actor"/> and <see cref="Actor2D"/>.
/// </summary>
internal static class SnapshotDiscard
{
    private static readonly System.Collections.Generic.HashSet<string> _logged = new();

    /// <summary>
    /// Returns true when <paramref name="property"/> is the snapshot key (the value is
    /// discarded and the event logged once per scene); false to fall through to base.
    /// <paramref name="value"/> mirrors the <c>_Set</c> signature and is intentionally
    /// unused — the snapshot is discarded in every build.
    /// </summary>
    public static bool TryHandle(Node root, StringName property, Variant value)
    {
        _ = value;
        if (property != Actor.SnapshotProperty)
            return false;
        Log(root);
        return true;
    }

    private static void Log(Node node)
    {
        var scene = node.SceneFilePath.Length > 0 ? node.SceneFilePath : node.Name.ToString();
        if (!_logged.Add(scene))
            return;

#if TOOLS
        GD.PushError($"[ActorLayout] '{Actor.SnapshotProperty}' reached _Set in an EDITOR build on '{scene}'. " +
            $"The [Export] in the actor's .Layout.cs is not registered (deleted, or TOOLS not defined). " +
            $"The snapshot will be dropped on next save and Reconstruct will fail for this scene.");
#else
        // Expected in export builds: the editor-only [Export] is stripped, so the serialized key
        // arrives here and is discarded. Not worth a Godot warning (backtrace spam on device);
        // trace at debug level only.
        cfEngine.Log.LogDebug($"[ActorLayout] '{scene}': discarding editor-only '{Actor.SnapshotProperty}' " +
            $"(no such property in export builds). Expected and harmless.");
#endif
    }
}
