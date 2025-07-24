using System;
using System.Threading;
using System.Threading.Tasks;
using cfEngine.Asset;
using Godot;

namespace cfGodotEngine.Asset;

public class ResourceAssetManager: AssetManager<Resource>
{
    private static readonly Action ReleaseAction = () => { };
    
    protected override AssetHandle<T> _Load<T>(string path)
    {
        var resource = ResourceLoader.Load<T>(path);
        return new AssetHandle<T>(resource, ReleaseAction);
    }

    protected override Task<AssetHandle<T>> _LoadAsync<T>(string path, CancellationToken token = default)
    {
        var resourceTask = AsyncResourceLoader.LoadAsync(path, null);
        return resourceTask.ContinueWith(t => new AssetHandle<T>((T)t.Result, ReleaseAction), token);
    }
}