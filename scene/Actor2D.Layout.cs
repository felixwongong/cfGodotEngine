#if TOOLS
using Godot;

namespace cfGodotEngine.SceneManagement;

public abstract partial class Actor2D
{
    [ExportGroup("DEBUG")]
    [Export] private Godot.Collections.Dictionary _layoutSnapshot = new();

    public bool ConstructLayout()
    {
        try
        {
            var t = new ActorBuilder(this);
            ConstructLayout(t);
            return t.Build();
        }
        catch (System.Exception ex)
        {
            GD.PushError($"{GetType().Name}.ConstructLayout: layout build threw: {ex}");
            return false;
        }
    }

    protected virtual void ConstructLayout(ActorBuilder t) { }

    public override void _ValidateProperty(Godot.Collections.Dictionary property)
    {
        if (property["name"].AsString() == Actor.SnapshotProperty)
        {
            var usage = property["usage"].As<int>();
            property["usage"] = Variant.From(usage | (int)(PropertyUsageFlags.Storage | PropertyUsageFlags.Editor | PropertyUsageFlags.ReadOnly));
        }
    }
}
#endif
