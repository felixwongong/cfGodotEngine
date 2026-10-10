# cfGodotEngine

This project adapts the C# `cfEngine` libraries for use inside the Godot engine. It includes a logger, asset loading helpers, a flexible settings system, optional Google Drive mirroring and utilities for building state machines.

Documentation is generated with DocFX. See the `doc` folder for markdown sources.

## Module standard

**Layer 3** of the cf* module standard — Godot adapters of the layers below.

- **Holds:** Godot adapters of the layers below (UIPanel/UIRoot, Actor2D, StateMachineNode, binding, scene/asset managers, GodotLogger, editor + debug tooling).
- **Must NOT hold:** game rules; logic testable without Godot (push it down).

Dependencies point **DOWN only**: `game → cfGodotEngine → cfGameEngine → cfEngine`. cfGodotEngine may reference cfEngine (and cfGameEngine) downward; it must never reference the game repo (enforced by the cross-module dependency-direction test). See [`.agent/architecture.md`](.agent/architecture.md) and [`Modules/cfEngine/skills/cf-coding-standard/SKILL.md`](../Modules/cfEngine/skills/cf-coding-standard/SKILL.md).
