using Godot;
using Godot.Collections;

namespace cfGodotEngine.Util;

public static class ExportUtil
{
    public static void MarkReadOnly(this Dictionary property, StringName propertyName)
    {
		if (property["name"].AsStringName() == propertyName)
		{
			property["usage"] = (int)(PropertyUsageFlags.Default | PropertyUsageFlags.ReadOnly);
		}
    }

    public static void EnsureSceneType<T>(this Dictionary property, StringName propertyName, ref PackedScene scene) where T : Node
    {
        if (property["name"].AsStringName() == propertyName && !IsSceneType<T>(scene))
        {
            scene = null;
        }
    }
    
    public static bool IsSceneType<T>(this PackedScene scene) where T : Node
    {
        if (scene == null)
            return false;
        
        var inst = scene.Instantiate();
        var result = inst is T;
        inst.QueueFree();
        return result;
    }
}