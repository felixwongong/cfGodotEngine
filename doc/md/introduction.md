# Introduction

`cfGodotEngine` provides helpers and integrations for the Godot engine.
It builds on top of the [cfEngine](https://github.com/cfengine/cfEngine) project, so make sure to reference those assemblies when building your game.

## Features

- `GodotLogger` implements the `ILogger` interface using Godot's logging functions.
- `ResourceAssetManager` and `AsyncResourceLoader` simplify loading `Resource` assets.
- A `Setting<T>` base class lets you load `.tres` resources with `[SettingPath]`.
- Optional Google Drive support via `DriveMirror`.
- Generic state machine nodes for building behaviors.

### Basic Usage

```csharp
ILogger logger = new GodotLogger();
logger.LogInfo("cfGodotEngine initialized");
```
