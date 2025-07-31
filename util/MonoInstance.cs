using Godot;

namespace cfGodotEngine.Util;

public abstract partial class MonoInstance<T> : Node where T: MonoInstance<T>, new()
{
    private static T _instance;

    public static T Instance
    {
        get
        {
            if (_instance != null) return _instance;

            _instance = new T();
            _instance.Name = $"_{typeof(T).Name}";
            
            NodeUtil.GetSceneTree().Root.AddChild(_instance);
            
            return _instance;
        }
    }

    protected MonoInstance()
    {
        _instance = this as T;
    }
}