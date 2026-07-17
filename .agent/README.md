# .agent — Agent context

Read this file first each session. These docs are the source of truth for cfGodotEngine context.

## Must read (every session)

1. [`project.md`](project.md) — what this framework is, stack, domain

Root pointer: [`AGENTS.md`](../AGENTS.md) in the framework root.

## Task routing

| If you are... | Read |
|---|---|
| Working on a subsystem | The subsystem's source doc or the relevant CatSweeper `systems/` doc |
| Changing how CatSweeper uses the framework | CatSweeper `.agent/systems/` and this `project.md` |
| Changing framework internals | `project.md` and source code under `cfGodotEngine/` |
| Open questions or blockers | [`pending.md`](pending.md) |

## Rules

- Keep docs in sync with code changes.
- Append one line to [`CHANGELOG.md`](CHANGELOG.md) per significant change if the file exists.
- Unknowns go in a `pending.md` if created; otherwise note them in the commit/PR.
