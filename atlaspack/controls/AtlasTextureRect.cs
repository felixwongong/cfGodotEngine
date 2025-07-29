using cfGodotEngine.Asset;
using Godot;

namespace cfGodotEngine.Controls;

[Tool]
[GlobalClass]
public partial class AtlasTextureRect: TextureRect
{
    [Export]
    private GDAtlasTextureRef textureRef
    {
        get => _textureRef;
        set
        {
            _textureRef = value;
            textureRef.Connect(GDAtlasTextureRef.SignalName.OnAtlasTextureUpdated, new Callable(this, nameof(UpdateTexture)));
        }
    }

    private GDAtlasTextureRef _textureRef;
    
    private void UpdateTexture(Texture2D texture)
    {
        Texture = texture;
    }
}