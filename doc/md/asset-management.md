# Asset Management

`ResourceAssetManager` extends `AssetManager<Resource>` so Godot resources can be loaded through the `cfEngine` asset pipeline.

Register the manager with your game:

```csharp
game.WithAsset(new ResourceAssetManager());
```

Load resources asynchronously via `AsyncResourceLoader`:

```csharp
var texture = await AsyncResourceLoader.LoadAsync("res://icon.png", null);
```
