using Godot;

namespace cfGodotEngine.SceneManagement;

#if TOOLS
[Tool]
#endif
[GlobalClass]
public abstract partial class Actor2D : Node2D
{
    public override bool _Set(StringName property, Variant value)
        => SnapshotDiscard.TryHandle(this, property, value) || base._Set(property, value);
}
