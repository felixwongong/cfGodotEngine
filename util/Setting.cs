using System;
using System.Reflection;
using Godot;

namespace cfGodotEngine.Util;

public class SettingPath : Attribute
{
    public readonly string path;

    public SettingPath(string path)
    {
        this.path = path;
    }
}

public abstract partial class Setting<T>: Resource where T : Setting<T>
{
    public static T GetSetting() 
    {
        if (typeof(T).GetCustomAttribute(typeof(SettingPath), true) is not SettingPath pathAttribute) 
        {
            GD.PushError($"Setting {typeof(T).Name} does not have a SettingPath attribute.");
            return null;
        }

        var path = pathAttribute.path;
        var setting = ResourceLoader.Load<T>(path);
        if (setting == null)
        {
            GD.PushError($"Setting {typeof(T).Name} not found in path ({path}).");
            return null;
        }
        return setting;
    }
}