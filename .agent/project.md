# Project

## What we are building

`cfGodotEngine` adapts the C# `cfEngine` libraries for use inside the Godot engine. It provides shared utilities for CatSweeper: logging, asset loading helpers, a flexible settings system, Google Sheets integration, and state-machine utilities.

## Stack

| Layer | Choice | Notes |
|---|---|---|
| Language | C# | Godot 4 / .NET 8 compatible |
| Parent core | cfEngine | Shared C# core library (subtree under `Modules/cfEngine`) |
| Docs | DocFX | Markdown sources under `doc/` |
| Consumer | CatSweeper | Imported as a Git subtree |

## Domain in one paragraph

The framework sits between Godot and the game-specific code. It wraps engine capabilities (logging, resource loading, reactive binding, settings) so that CatSweeper gameplay code can stay focused on game logic. Changes here affect every CatSweeper feature that relies on the framework.

## Non-obvious constraints

- Checked out inside CatSweeper as a Git subtree. Only the programmer/owner pushes changes back to `https://github.com/felixwongong/cfGodotEngine.git` via `Tools/subtree.ps1`.
- Any breaking change must be coordinated with CatSweeper builds (`dotnet build CatSweeper.sln`).
- See `doc/` for full framework documentation generated with DocFX.

## CatSweeper usage

CatSweeper references this framework for:

- Reactive UI binding (`binding/`).
- Logging (`util/` or `log/`).
- Settings and Google Sheets helpers.
- Scene management helpers.

### Scene management contract

`ISceneManager<TScene>` (`scene/ISceneManager.cs`, implemented by `scene/GodotSceneManager.cs`)
owns Single/Additive scene loading on the Godot main thread:

- `LoadScene(key, LoadSceneMode.Single)` evicts the previous current scene, attaches the new scene
  under `SceneTree.Root`, and assigns it as `SceneTree.CurrentScene`. Additive loads attach under
  `Root` only and never touch `CurrentScene`.
- `UnloadCurrent()` synchronously detaches the current Single scene (if any), releases
  `SceneTree.CurrentScene`, clears tracked-scene bookkeeping, and queues the node for **deferred**
  destruction with `QueueFree()`. Return means the scene is *detached*, not destroyed — Godot frees
  it later. No current scene is a safe no-op, and repeated calls never free the same scene twice.
  Persistent root children (e.g. `UIRoot` and registered services) are never bulk-cleared; only the
  current scene and tracked scenes are evicted.
- Eviction does **not** cancel, clear, or await in-flight `LoadSceneAsync` / preload work, and it
  cannot evict untracked additive scenes (`activeSceneSet` is not populated today). Callers needing
  a stable detached state must let outstanding loads settle first; an unsettled load may still
  attach a scene afterwards.
- All calls must run on the Godot main thread. Detachment runs immediately and can trigger exit
  callbacks even though destruction is deferred.

See CatSweeper's `.agent/systems/subtrees.md` for the subtree workflow.
