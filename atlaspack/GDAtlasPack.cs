using Godot;
using Godot.Collections;

namespace cfGodotEngine.Asset;

[Tool]
[GlobalClass]
public partial class GDAtlasPack: Resource
{
    [Export] public Array<GDAtlas> atlasList = new();

    public void AddPack(string atlasId, AtlasContext context, Texture2D atlasTexture)
    {
        var atlas = new GDAtlas();
        atlas.atlasId = atlasId;
        atlas.dimension = new Vector2(context.bounds.width, context.bounds.height);
        
        if (atlasList == null)
            atlasList = new Array<GDAtlas>();
        atlasList.Add(atlas);
        
        atlas.atlasTexture = atlasTexture;
    }
}