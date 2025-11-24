using Godot;
using Godot.Collections;

namespace cfGodotEngine.Asset;

[Tool]
[GlobalClass]
public partial class GDAtlas: Resource
{
    [Export] public string atlasId;
    [Export] public Vector2 dimension;
    [Export] public Godot.Collections.Dictionary<string, AtlasTexture> imageMap;
    [Export] public Texture2D atlasTexture;
}