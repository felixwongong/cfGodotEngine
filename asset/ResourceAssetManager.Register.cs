using cfEngine.Asset;
using cfGodotEngine.Asset;
using Godot;

namespace cfEngine.Core;

public static partial class GameExtension
{
    public static Game WithAsset(this Game game, ResourceAssetManager assetManager)
    {
        game.Register(assetManager, nameof(ResourceAssetManager));
        return game;
    }
    
    public static AssetManager<Resource> GetAsset(this Game game)
    {
        return game.GetService<ResourceAssetManager>(nameof(ResourceAssetManager));
    }
}