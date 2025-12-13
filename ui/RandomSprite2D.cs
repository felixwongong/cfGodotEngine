using System;
using Godot;

namespace cfGodotEngine.UI;

public partial class RandomSprite2D: Sprite2D
{
    private static readonly Random rng = new();
    
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

        var index = rng.Next(0, _textures.textures.Count);
        Texture = _textures.textures[index];
    }

    public void SelectNext()
    {
        var index = rng.Next(0, _textures.textures.Count);
        Texture = _textures.textures[index];
    }
}