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
        var imageMap = new Dictionary<string, Rect2I>(); 
        foreach (var (imageName, rect) in context.imageRectMap)
        {
            imageMap.Add(imageName, new Rect2I(rect.x, rect.y, rect.width, rect.height));
        }
        atlas.imageMap = imageMap;
        
        if (atlasList == null)
            atlasList = new Array<GDAtlas>();
        atlasList.Add(atlas);
        
        atlas.atlasTexture = atlasTexture;
    }
}