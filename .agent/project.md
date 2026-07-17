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

See CatSweeper's `.agent/systems/subtrees.md` for the subtree workflow.
