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
- `LoadSceneAsync(key, mode, progress)` returns a task that completes **only after** the scene has
  been instantiated and attached — a successful task means an attached scene, not a pending load.
  Entry requires the Godot main thread; the caller's current synchronization context is captured at
  entry and both the cached and cold attachment continuations are scheduled on it, so attachment
  always runs on the Godot main thread regardless of which thread/branch completes the underlying
  `PackedScene` load.
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
- All calls must run on the Godot main thread. Every tree-mutating entry point (`LoadScene`,
  `LoadSceneAsync`, `UnloadCurrent`, `ShowScene`) fail-fast-rejects a wrong-thread caller with an
  `InvalidOperationException` **before** any cache/accounting or tree access; `LoadSceneAsync` throws
  synchronously (it is not `async`) and, on entry, rejects a missing synchronization context rather
  than falling back to a worker thread. Detachment runs immediately and can trigger exit callbacks
  even though destruction is deferred.

### UI panel visibility contract

`UIPanel` (`ui/UIPanel.cs`, extending `ui/UIComponent.cs`) is the screen abstraction and **owns the
visibility of the `CanvasLayer` content canvases that belong to it**:

- A screen's content normally lives under a child `CanvasLayer` (`Layer`). A `CanvasLayer` is a
  `Node`, not a `CanvasItem`, so hiding the panel's root `Control` does not suppress that separate
  canvas — the framework therefore synchronizes every panel-owned descendant `CanvasLayer` from the
  panel's **effective** visibility via `_Notification(NotificationVisibilityChanged)`. `Show()`/`Hide()`
  remain plain base calls; the notification path also covers direct `Visible` assignment and calls
  made through a `Control` reference. This is what stops a "hidden" screen from continuing to render
  (registry defect `ui-panel-canvaslayer-leak`).
- Effective visibility is the panel's own `Visible` combined with every ancestor `CanvasItem.Visible`
  up to the tree root (the raw node parent chain is walked because a `CanvasLayer` boundary severs
  native `is_visible_in_tree()` propagation). The gate is recomputed on each sync from panel/ancestor
  state — never from a layer's previously forced value — so `Hide()` cannot latch a layer false.
- Traversal is recursive and includes internal children, covering multiple sibling layers and layers
  nested below other layers or ordinary `Node`s. A nested `UIPanel` keeps ownership of its own
  canvases: an enclosing panel hands it the enclosing gate only, and the nested panel's own hidden
  flag wins. Descendant `Control.Visible` flags, binding sources, and scene ownership are left
  untouched, so independently hidden widgets stay hidden.
- Lifecycle: the panel syncs on `_EnterTree`/`_Ready` (so a panel restored or re-entered reconciles
  immediately), subscribes to ancestor `CanvasItem.VisibilityChanged` and to `SceneTree.NodeAdded`/
  `SceneTree.TreeChanged` while in-tree (reconciling dynamically added layers and reparenting before
  the next render), guards reentrancy with a private flag, and unsubscribes both ancestor and tree
  signals in `_ExitTree` with no retained `SceneTree` reference. No per-frame scanning is used.

See CatSweeper's `.agent/systems/subtrees.md` for the subtree workflow.
