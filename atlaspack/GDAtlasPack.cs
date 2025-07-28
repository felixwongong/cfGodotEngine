using System;
using System.IO;
using cfGodotEngine.Util;
using Godot;
using Godot.Collections;

namespace cfGodotEngine.Asset;

[Tool]
[GlobalClass]
public partial class GDAtlasPack: Resource
{
    private static readonly string atlasCachePath = "res://.godot/atlas_cache/";
    
    [Export] public Array<GDAtlas> atlasList = new();

    public void AddPack(AtlasPack pack)
    {
        var context = pack.context;
        var imageData = pack.image.imageData;

        var atlas = new GDAtlas();
        atlas.atlasId = Guid.NewGuid().ToString();
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
        var texturePath = $"{atlasCachePath}{atlas.atlasId}.png";

        Directory.CreateDirectory(Application.GetGlobalizePath(atlasCachePath));
        ResourceSaver.Save(texture, texturePath);
        atlas.atlasImage = texture;
    }
}