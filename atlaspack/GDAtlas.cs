using System;
using Godot;

namespace cfGodotEngine.Asset;

[Tool]
[GlobalClass]
public partial class GDAtlas: Resource
{
    [Export] public string atlasId;
    [Export] public Vector2 dimension;
    [Export] public Texture2D atlasImage;
}