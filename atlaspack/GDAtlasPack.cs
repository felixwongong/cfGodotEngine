using Godot;
using Godot.Collections;

namespace cfGodotEngine.Asset;

[Tool]
[GlobalClass]
public partial class GDAtlasPack: Resource
{
    [Export] public Array<GDAtlas> atlasList = new();

    public void AddPack(AtlasPack pack)
    {
        var context = pack.context;
        var imageData = pack.image.imageData;
        
        var atlas = new GDAtlas();
        atlas.dimension = new Vector2(context.bounds.width, context.bounds.height);
        
        if (atlasList == null)
            atlasList = new Array<GDAtlas>();
        atlasList.Add(atlas);
        
        var image = new Image();
        var imageLoadError = image.LoadPngFromBuffer(imageData);
        if (imageLoadError != Error.Ok)
        {
            GD.PushError($"Failed to load image from buffer: {imageLoadError}");
            return;
        }

        var texture = ImageTexture.CreateFromImage(image);
        var texturePath = $"res://.godot/atlas_cache/{GetName()}.png";
        ResourceSaver.Save(texture, texturePath);
        atlas.atlasImage = texture;
    }
}