using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using cfEngine.Info;
using cfEngine.Logging;
using cfEngine.Pooling;
using Godot;

namespace cfGodotEngine.Info;

public class ResourceInfoLoader<TInfo>: IValueLoader<TInfo>
{
    private readonly ResourceInfo<TInfo> _resourceInfo;

    public ResourceInfoLoader(string resourcePath)
    {
        _resourceInfo = GD.Load<ResourceInfo<TInfo>>(resourcePath);
        if (_resourceInfo == null)
        {
            Log.LogException(new System.ArgumentNullException(nameof(_resourceInfo), $"Resource not found at path: {resourcePath}"));
        }
    }
    
    public ObjectPool<List<TInfo>>.Handle Load(out List<TInfo> values)
    {
        var handle = ListPool<TInfo>.Default.Get(out values);

        if (_resourceInfo == null) 
        {
            Log.LogError("ResourceInfo is null, cannot load values.");
            return handle;
        }
        
        values.AddRange(_resourceInfo.GetInfos);
        return handle;
    }

    public Task<List<TInfo>> LoadAsync(CancellationToken cancellationToken)
    {
        ListPool<TInfo>.Default.Get(out var values);

        if (_resourceInfo == null) 
        {
            Log.LogError("ResourceInfo is null, cannot load values.");
            return Task.FromResult(values);
        }
        
        values.AddRange(_resourceInfo.GetInfos);
        return Task.FromResult(values);
    }
}