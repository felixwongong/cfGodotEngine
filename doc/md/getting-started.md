# Getting Started

Utilities in this repository can be added to an existing Godot project.
Before you start, clone the **cfEngine** repository from [GitHub](https://github.com/cfengine/cfEngine) and include its assemblies in your project. cfGodotEngine extends cfEngine in a similar way to how Unity packages build upon the core engine.


## Asset Management

Register `ResourceAssetManager` with your `Game`:

```csharp
game.WithAsset(new ResourceAssetManager());
```
### Example Game Class

```csharp
public partial class MyGame : Game
{
    public override void Initialize()
    {
        WithLogger(new GodotLogger());
        WithAsset(new ResourceAssetManager());
    }
}
```


Use `AsyncResourceLoader` for asynchronous loading:

```csharp
var task = AsyncResourceLoader.LoadAsync("res://path/to/resource.tres", null);
```

## Google Drive Mirror

`DriveMirrorSetting` is loaded from `res://Settings/GoogleDrive/DriveMirrorSetting.tres`.
Refresh the mirror with progress feedback:

```csharp
await DriveMirror.instance.RefreshWithProgressBar();
```
