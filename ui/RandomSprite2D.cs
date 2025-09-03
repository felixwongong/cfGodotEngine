using Godot;

namespace cfGodotEngine.UI;

public partial class RandomSprite2D: Sprite2D
{
    private static readonly RandomNumberGenerator rng = new();
    
    [Export] private Texture2DList _textures;
    
    public override void _Ready()
    {
        base._Ready();
        
        if (Engine.IsEditorHint())
            return;
        
        if (_textures == null || _textures.textures.Count == 0)
        {
            GD.PrintErr("RandomSprite2D: No textures assigned.");
            return;
        }

        var index = rng.RandiRange(0, _textures.textures.Count - 1);
        Texture = _textures.textures[index];
    }

    public void SelectNext()
    {
        var index = rng.RandiRange(0, _textures.textures.Count - 1);
        Texture = _textures.textures[index];
    }
}