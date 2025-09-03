using Godot;
using Godot.Collections;

namespace cfGodotEngine.UI;

[Tool]
[GlobalClass]
public partial class Texture2DList: Resource
{
    [Export] public Array<Texture2D> textures;
}