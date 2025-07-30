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
            if (_textureRef != null)
                _textureRef.OnAtlasTextureUpdated -= UpdateTexture;
            _textureRef = value;
            if (_textureRef != null)
                textureRef.OnAtlasTextureUpdated += UpdateTexture;
        }
    }

    private GDAtlasTextureRef _textureRef;
    
    private void UpdateTexture(Texture2D texture)
    {
        Texture = texture;
    }
}