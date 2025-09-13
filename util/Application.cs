using Godot;

namespace cfGodotEngine.Util;

public static class Application 
{
    public static string assetDataPath => ProjectSettings.GlobalizePath("res://");
    public static string exportDataPath => ProjectSettings.GlobalizePath("res://_Export/");
    public static string persistentDataPath => ProjectSettings.GlobalizePath("user://");
    public static string GetGlobalizePath(string path) => ProjectSettings.GlobalizePath(path);

    public static void Quit()
    {
        const int NotificationWMCloseRequest = (int)1017L;
        NodeUtil.GetSceneTree().Root.PropagateNotification(NotificationWMCloseRequest);   
    }
}