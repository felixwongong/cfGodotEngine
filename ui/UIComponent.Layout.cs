#if TOOLS
using cfGodotEngine.SceneManagement;
using Godot;

namespace cfGodotEngine.UI;

public partial class UIComponent
{
    [ExportGroup("DEBUG")]
    [Export] private Godot.Collections.Dictionary _layoutSnapshot = new();

    public void ConstructLayout()
    {
        var t = new ActorBuilder(this);
        ConstructLayout(t);
        if (!t.Build())
            GD.PushError($"{GetType().Name}.ConstructLayout: layout build failed; close the scene without saving.");
    }

    protected virtual void ConstructLayout(ActorBuilder t) { }

    public override void _ValidateProperty(Godot.Collections.Dictionary property)
    {
        base._ValidateProperty(property);
        if (property["name"].AsString() == Actor.SnapshotProperty)
            property["usage"] = Variant.From((int)(PropertyUsageFlags.Storage | PropertyUsageFlags.Editor | PropertyUsageFlags.ReadOnly));
    }
}
#endif
