# Getting Started

Utilities in this repository can be added to an existing Godot project.

## Asset Management

Register `ResourceAssetManager` with your `Game`:

```csharp
game.WithAsset(new ResourceAssetManager());
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
