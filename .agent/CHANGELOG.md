# CHANGELOG

2026-08-04 — Static collection row reconciliation now logs expected spawn/remove operations at debug level instead of warning level; missing/invalid collection setup remains warning/error-worthy.

2026-08-02 — Binding diagnostics now tolerate detached collection rows, and all `BindingBase` subclasses defer unresolved null `PropertyMap`s (shared `GetReadyPropertyMap`) until the injected source is ready.

2026-07-17 — Add `.agent/` docs for cfGodotEngine: README, project.md, AGENTS.md.
