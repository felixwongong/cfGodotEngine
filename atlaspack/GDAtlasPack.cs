using Godot;
using Godot.Collections;

namespace cfGodotEngine.Asset;

[Tool]
[GlobalClass]
public partial class GDAtlasPack: Resource
{
    [Export] public Array<GDAtlas> atlasList = new();

    public void AddContext(AtlasContext context)
    {
        var atlas = new GDAtlas();
        atlas.dimension = new Vector2(context.bounds.width, context.bounds.height);
        
        if (atlasList == null)
            atlasList = new Array<GDAtlas>();
        atlasList.Add(atlas);
    }
}