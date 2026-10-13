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

### Event binding contract

`cfGodotEngine.Binding` carries a UI→source **event channel** beside the one-way source→UI
property/collection channels. It is hand-authored, not generator-emitted:

- A source opts in by implementing `IEventSource` (`binding/IEventSource.cs`) next to its
  `[BindingSource]` attribute: `IEnumerable<string> GetEventKeys()` and `IEventMap GetEvents`
  (`binding/IEventMap.cs`). There is no `static abstract` member and no registry — the
  `BindingSourceGenerator` has no event support yet (it lives in a different subtree), so a source
  declares `public readonly BindableEvent Foo = new();` / `BindableEvent<int> Bar = new();` members
  and builds `EventMap.Build().Add(nameof(Foo), Foo).Add(nameof(Bar), Bar).Build()`
  (`binding/EventMap.cs`, `binding/BindableEvent.cs`). `GetEvents` may return `null` while the source
  is not ready — `null` means *pending*, the null-`PropertyMap` rule.
- `EventBinding` (`binding/EventBinding.cs`, a `BindingBase` node, **not** `[Tool]`) is added as a
  child of the emitting control. The target convention mirrors `Binding`: the watched node is always
  the **parent** (no NodePath). It exports a signal name (default `pressed`) and an optional argument;
  on the parent's signal it calls `GetEvents.TryEmit(key, arg)` and contains a handler/coercion
  exception with an ungated `Log.LogException`. It is inert in the editor
  (`Engine.IsEditorHint()`), so an editor press never mutates game state, and it disconnects
  deterministically in `_ExitTree`.
- **Round-trip (intended).** UI signal → `EventBinding` emits the named source event (with payload) →
  the source's subscribed handler mutates its own `Bindable<T>` state → its `PropertyMap` raises
  change keys → existing one-way `Binding` nodes re-render. A handler that mutates state outside its
  bindables leaves the UI stale — a source-authoring contract, not framework-enforced. Events
  dispatch synchronously on the signal handler's (main) thread and do **not** interact with
  `IBindingSource.BeginUpdate()`; batching stays a tool the handler uses internally.
- **Events are verbs, not state.** `BindableEvent` / `BindableEvent<T>` deliberately do **not**
  implement `IBindableBase`, so they can never enter a `PropertyMap` or the binding-key list.
- **The payload channel is `object`.** `IEventMap.TryEmit(string, object)` carries the payload;
  `BindableEvent<T>` erases once at the framework boundary through the `internal
  IBindableEventUntyped` (the same single-erasure-point doctrine as `IBindableUntyped.cs`): an exact
  `T` is forwarded as-is, `null` is forwarded as `default(T)` only when `T` is null-representable
  (reference type or `Nullable<>`), and otherwise a **narrow** `Convert.ChangeType` coercion applies —
  it exists because a scene-serialized Variant int arrives as a boxed `long`, so `payload is int`
  would fail for a numeric picker. Anything richer is a source-authoring contract: author the wider
  `T`, or convert in the handler.
- Subscriptions are weak-referenced `Relay` subscriptions — retain the `Subscription` handle returned
  by `BindableEvent.Subscribe`.

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

### Debug UI scale contract

`DebugUiScale` (`debug/DebugUiScale.cs`, namespace `cfGodotEngine.Debug`, always compiled) is the
single knob for debug-UI density (`debug_window/*`, `command_console/CommandConsoleOverlay`):

- `Factor` is 1 on desktop and 2 on touch/phone-class screens. `S(int)` / `S(float)` multiply a base
  pixel constant by `Factor`; every swept sizing expression scales exactly once at consumption, and
  the base constants are unchanged.
- Classification is pure and engine-free in `GetFactorFor(mode, touch)` / `IsPhoneFor(mode, touch,
  width, height)`. The native `Factor` / `IsPhoneMode` read `DisplayServer.IsTouchscreenAvailable()`
  and `DisplayServer.ScreenGetSize()` on the Godot main thread only — never on a worker thread and
  never from a static type initializer.
- Mode comes from the `debug/ui_scale_mode` project setting (`auto` | `desktop` | `mobile`) and is
  resolved on every read, so no native result is cached. Missing/blank/invalid values fall back to
  `auto`. The setting is registered with `ProjectSettings.SetSetting` **only when absent**, together
  with an enum `auto,desktop,mobile` property hint; `ProjectSettings.Save()` is never called and the
  framework never edits `project.godot`.
- Mode semantics: `desktop` forces ×1 and is never phone; `mobile` forces ×2 and is phone even on a
  desktop; `auto` is ×2 when touch and phone only when touch **and** the shorter physical screen edge
  is ≤1200 px (rotation-insensitive; nonpositive screen dimensions are never phone). A touch tablet
  therefore scales ×2 but is not phone.
- At ×2, fonts/constants an element would otherwise inherit are scaled by `ScaleInheritedFont` /
  `ScaleInheritedConstant`, derived from the value already resolved for that element; at ×1 no extra
  override is installed.

This is the density half of run `debug-ui-mobile` (**vs-1**). The fullscreen/phone half (**vs-2**)
is the contract below; vs-1 alone did **not** add safe-area insets, drag/cascade suppression or the
stacked phone window mode.

#### Phone fullscreen debug windows (vs-2 of run `debug-ui-mobile`)

`DebugWindowHost` (`debug/debug_window/DebugWindowHost.cs`, Layer 126) resolves
`DebugUiScale.IsPhoneMode` **once** in `_Ready` on the Godot main thread. Phone mode is therefore
fixed per host — a preview change needs a fresh UI/restart (no live rescaling). On a phone-class
screen the host replaces floating cards with a single fullscreen window:

- **Placement is the mapped display safe area, never a ratio.** The host reads
  `DisplayServer.GetDisplaySafeArea()` (replaceable only by the compiled editor probe through the
  `internal` instance `SafeAreaReader` seam — never a setting, static or game-visible override) and
  calls the pure, `internal static`
  `MapDisplaySafeAreaToHost(safeDisplay, visibleHost, hostToScreen)`. The caller derives
  `hostToScreen = panel.GetScreenTransform() * panel.GetTransform().AffineInverse()`, which cancels
  the card's own translation and already folds in window position, `canvas_items` stretch and
  orientation. All four corners of the safe rect are transformed by the affine inverse, the min/max
  rectangle is intersected with `Viewport.GetVisibleRect()`, and the result is assigned as the whole
  card rect. The mapper never multiplies by `DebugUiScale.Factor`, never subtracts a window origin a
  second time, never applies a camera transform and never rounds insets before conversion.
- **Fallback**: an empty/nonpositive/non-finite safe rect, a non-finite or singular transform, a
  nonpositive visible rect or an empty intersection yields the full visible host rect. A headless
  SubViewport (no real display mapping) therefore lands on the full visible rect rather than the root
  display's safe area — an invalid/unsupported native rect is a fallback, **not** evidence of notch
  protection. Arbitrarily rotated/skewed host layers are neither introduced nor claimed supported.
- **Safe-area changes without a viewport resize** are picked up by a per-frame main-thread
  recomputation that writes placement only when the mapped rectangle actually changes; a viewport
  resize additionally schedules a forced deferred recomputation after stretch/layout settles.
- **No drag, no cascade, no ratio sizing.** Phone `PositionCard` assigns the safe rect Position/Size
  and returns before the cascade arithmetic (it never writes the cascade origin); `DesiredSize` is
  bypassed entirely (no 92 %/65 % ratios, no width/height caps); `MoveCard` clears press/drag state
  and refuses motion; `OnHeaderGuiInput` clears press/drag/distance and returns before any mouse
  handling, so neither native touch nor emulated mouse can move or reorder the card. The close button
  keeps its own `Pressed` hookup and content keeps its `ScrollContainer`.
- **Stacking reuses sibling order** — there is no second stack store. A newly attached card is
  brought to front; an existing reopen uses `BringToFront` without re-running the factory; after
  every open/reopen/close exactly the last live dictionary-owned card is `Visible`. Lower cards stay
  in-tree with their content, selection and scroll state, hidden and input-inactive. The closing card
  is removed from the dictionary and hidden **before** `QueueFree`, so a deferred free can never leave
  it selectable as the top. Closing a non-top card leaves the top untouched; closing the last card
  leaves zero live cards and no stranded surface. Reopen after a real close builds fresh content (a
  new instance).
- **Phone-only chrome filters**: the panel/layout/header/scroll chain explicitly keeps
  `MouseFilter = Stop`. A plain `Control` defaults to `Stop` but `Container` subclasses default to
  `Pass`, so the blocking filter is stated rather than inherited; the title label and drag spacer stay
  `Ignore` and the close control stays a real `Button`. The desktop tree adds no explicit filters and
  no full-viewport STOP wrapper, and game input dispatch is not migrated.
- **Desktop and touch tablets are unchanged**: floating placement, 32 px cascade, drag and clamp all
  keep their original expressions, and the desktop probe JSON stays byte-identical at 1080×2560 and
  1920×1920.

Explicit limits and held evidence (no blanket "mobile usable" or "safe area verified" claim):

- The host stays at Layer **126** and `CommandConsoleOverlay` at **128**: the console DEBUG entry
  renders and takes input above the fullscreen card and may deliberately overlap its content; the
  console itself does not respect display cutouts and is not safe-area redesigned. No automatic
  console hiding, no switcher, no backdrop and no layer change is added.
- **Horizontal overflow is measured, real and accepted.** At 1080×2560 with zero safe insets the
  phone panel is **1080** wide; the 24 px content margins leave **1032** logical px (less the settled
  vertical-scrollbar width), while the viewer's intrinsic content is **1055** px (action row:
  `Changes only` ≈388 + `Copy TSV` ≈255 + `Copy changes` ≈380 + two 16 px gaps) — a 23 px overflow —
  and the four-column table needs **1944** px. Table horizontal scrolling is required and works
  (programmatic scroll-to-max reaches the last header cell and the table's right edge); the action-row
  overflow is clipped at the card edge. Real six-tab/device reachability is a human hold.
- Human holds, not inferred: real cutout/notch behavior (including double-inset avoidance and rotation
  with two windows open), physical tap/scroll/edit routing and board-input resumption after closing
  the last card, console overlap/keyboard occlusion, visible-window pixel parity, native tablet
  classification, and the `debug/ui_scale_mode` Project-Settings discoverability carried from vs-1.

See CatSweeper's `.agent/systems/subtrees.md` for the subtree workflow.
