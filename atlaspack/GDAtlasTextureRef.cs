using Godot;

namespace cfGodotEngine.Asset;

[Tool]
[GlobalClass]
public partial class GDAtlasTextureRef: Resource
{
    [Signal]
    public delegate void OnAtlasTextureUpdatedEventHandler(AtlasTexture texture);

    [Export]
    public GDAtlasPack atlasPack
    {
        get => _atlasPack;
        set
        {
            _atlasPack = value;
            OnTextureUpdate();
        }
    }

    [Export]
    public string imageName
    {
        get => _imageName;
        set 
        {
            _imageName = value;
            OnTextureUpdate();
        }
    }

    private GDAtlasPack _atlasPack; 
    private string  _imageName;
    
    private void OnTextureUpdate()
    {
        if (atlasPack == null || string.IsNullOrEmpty(imageName))
        {
            EmitSignal(SignalName.OnAtlasTextureUpdated, null);
            return;
        }

        foreach (var atlas in _atlasPack.atlasList)
        {
            if(atlas.imageMap.TryGetValue(imageName, out var texture))
            {
                EmitSignal(SignalName.OnAtlasTextureUpdated, texture);
                return;
            }
        }

        EmitSignal("OnAtlasTextureUpdated", null);
    }

}