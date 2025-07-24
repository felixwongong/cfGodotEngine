# Logging

`GodotLogger` implements the `cfEngine.Logging.ILogger` interface and routes messages to Godot's built‑in log functions.

## Usage

```csharp
ILogger logger = new GodotLogger();
logger.LogInfo("Initialized");
```

`AsyncResourceLoader` and other components accept an `ILogger` to surface warnings and errors while running in the engine.
