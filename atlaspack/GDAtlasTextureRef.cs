using System;
using cfEngine.Rx;
using Godot;

namespace cfGodotEngine.Asset;

[Tool]
[GlobalClass]
public partial class GDAtlasTextureRef: Resource
{
    private Relay<AtlasTexture> _onAtlasTextureUpdated;
    public event Action<AtlasTexture> OnAtlasTextureUpdated
    {
        add
        {
            _onAtlasTextureUpdated ??= new Relay<AtlasTexture>(this);
            _onAtlasTextureUpdated.AddListener(value);
        }
        remove => _onAtlasTextureUpdated?.RemoveListener(value);
    }

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
            _onAtlasTextureUpdated?.Dispatch(null);
            return;
        }

        foreach (var atlas in _atlasPack.atlasList)
        {
            if(atlas.imageMap.TryGetValue(imageName, out var texture))
            {
                _onAtlasTextureUpdated?.Dispatch(texture);
                return;
            }
        }

        _onAtlasTextureUpdated?.Dispatch(null);
    }
}