using Godot;

namespace cfGodotEngine.Util;

public static class Application 
{
    public static string assetDataPath => ProjectSettings.GlobalizePath("res://");
    public static string exportDataPath => ProjectSettings.GlobalizePath("res://_Export/");
    public static string persistentDataPath => ProjectSettings.GlobalizePath("user://");
}