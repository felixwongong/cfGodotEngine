using System;
using System.Collections.Generic;
using cfEngine.Rx;
using Godot;

namespace cfGodotEngine.Asset;

[Tool]
[GlobalClass]
public partial class GDAtlasTextureRef: Resource
{
    private Relay<AtlasTexture> _onAtlasTextureUpdated;
    private readonly Dictionary<Action<AtlasTexture>, Subscription> _listenerSubscriptions = new();

    public event Action<AtlasTexture> OnAtlasTextureUpdated
    {
        add
        {
            _onAtlasTextureUpdated ??= new Relay<AtlasTexture>(this);
            var subscription = _onAtlasTextureUpdated.AddListener(value);
            if (subscription != null) // null = duplicate listener (AddListener already logged an error); keep the live entry
            {
                _listenerSubscriptions[value] = subscription;
            }
        }
        remove
        {
            if (_listenerSubscriptions.TryGetValue(value, out var subscription))
            {
                _listenerSubscriptions.Remove(value);
                subscription.Unsubscribe();
            }
        }
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
