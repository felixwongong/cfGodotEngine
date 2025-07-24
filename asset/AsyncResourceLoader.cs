using System;
using System.Threading.Tasks;
using cfEngine.Logging;
using cfEngine.Pooling;
using cfGodotEngine.Util;
using Godot;
using Array = Godot.Collections.Array;

namespace cfGodotEngine.Asset;

public partial class AsyncResourceLoader
{
    public static Task<Resource> LoadAsync(string path, in IProgress<float> progress, string typeHint = "",
        bool useSubThread = false, ResourceLoader.CacheMode cacheMode = ResourceLoader.CacheMode.Reuse)
    {
        return Instance.Load(path, progress, typeHint, useSubThread, cacheMode);
    }
}

public partial class AsyncResourceLoader : MonoInstance<AsyncResourceLoader>
{
    private System.Collections.Generic.Dictionary<string, (IProgress<float> progress, TaskCompletionSource<Resource>
        taskSourceObject)> _loadingTasks = new();

    private ILogger logger;

    public Task<Resource> Load(string path, in IProgress<float> progress, string typeHint = "",
        bool useSubThread = false, ResourceLoader.CacheMode cacheMode = ResourceLoader.CacheMode.Reuse)
    {
        if (_loadingTasks.TryGetValue(path, out var x))
            return x.taskSourceObject.Task;

        var completionSource = new TaskCompletionSource<Resource>();
        ResourceLoader.LoadThreadedRequest(path, typeHint, useSubThread, cacheMode);
        _loadingTasks[path] = (progress, completionSource);

        SetProcess(true);
        return completionSource.Task;
    }

    private readonly Array progressArray = new(new[] { (Variant)0 });

    public override void _Process(double delta)
    {
        base._Process(delta);

        if (_loadingTasks.Count <= 0)
        {
            SetProcess(false);
            return;
        }

        using var handle = ListPool<string>.Default.Get(out var keyToRemove);
        foreach (var (path, (progress, taskSourceObject)) in _loadingTasks)
        {
            var status = ResourceLoader.LoadThreadedGetStatus(path, progressArray);
            switch (status)
            {
                case ResourceLoader.ThreadLoadStatus.InProgress:
                    progress?.Report(progressArray[0].AsSingle());
                    continue;
                case ResourceLoader.ThreadLoadStatus.Failed:
                case ResourceLoader.ThreadLoadStatus.InvalidResource:
                    progress?.Report(1);
                    taskSourceObject.TrySetException(new AsyncLoadFailedException(status));
                    keyToRemove.Add(path);
                    logger?.LogError($"Async resource load failed for path: {path} with status: {status}");
                    continue;
                case ResourceLoader.ThreadLoadStatus.Loaded:
                    progress?.Report(1);
                    var resource = ResourceLoader.LoadThreadedGet(path);
                    if (resource == null)
                    {
                        taskSourceObject.TrySetException(
                            new AsyncLoadFailedException(ResourceLoader.ThreadLoadStatus.InvalidResource));
                        logger?.LogError($"Async resource load returned null for path: {path}");
                    }
                    else
                    {
                        taskSourceObject.TrySetResult(resource);
                        logger?.LogInfo($"Async resource loaded successfully for path: {path}");
                    }

                    keyToRemove.Add(path);
                    continue;
            }
        }

        foreach (var key in keyToRemove)
        {
            _loadingTasks.Remove(key);
        }
    }

    public void SetLogger(ILogger logger)
    {
        this.logger = logger;
    }
}

class AsyncLoadFailedException : Exception
{
    private readonly ResourceLoader.ThreadLoadStatus _status;

    public AsyncLoadFailedException(ResourceLoader.ThreadLoadStatus status)
    {
        _status = status;
    }

    public override string Message => $"Async resource load failed with status: {_status}.";
}