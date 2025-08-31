using Godot;

namespace cfGodotEngine.Util;

public static class ObjectUtil
{
    public static bool IsAlive(this GodotObject o) => GodotObject.IsInstanceValid(o) && !((o as Node)?.IsQueuedForDeletion() ?? false);
}