using cfEngine.Asset;
using cfEngine.Core;
using cfGodotEngine.Asset;
using Godot;

namespace cfGodotEngine.Core;

public static partial class GameExtension
{
    public static Domain WithAsset(this Domain domain, ResourceAssetManager assetManager)
    {
        domain.Register(assetManager, nameof(ResourceAssetManager));
        return domain;
    }
    
    public static AssetManager<Resource> GetAsset(this Domain domain)
    {
        return domain.GetService<ResourceAssetManager>(nameof(ResourceAssetManager));
    }
}