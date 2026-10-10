using Godot;

namespace cfGodotEngine.SceneManagement;

#if TOOLS
[Tool]
#endif
[GlobalClass]
public abstract partial class Actor : Node
{
    public const string SnapshotProperty = "_layoutSnapshot";

    public override bool _Set(StringName property, Variant value)
        => SnapshotDiscard.TryHandle(this, property, value) || base._Set(property, value);
}
