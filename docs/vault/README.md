# HRM Project Vault

An Obsidian vault used as **shared persistent memory** for humans and agents working on the HRM SaaS platform. Open this folder in Obsidian via *Open folder as vault*.

This vault is committed to the repo — treat it as project knowledge, not a personal scratchpad. Do not put secrets here.

## How agents use this vault

Agents (`@business-analyst`, `@frontend-dev`, `@backend-dev`, `@qa-engineer`) read and write notes here as plain markdown. The folder layout is the contract — agents look in fixed locations rather than guessing.

| Folder | Purpose | Lifetime |
|---|---|---|
| `agents/` | Per-agent long-lived notes — preferences, working patterns, gotchas the agent should remember next session | Persistent |
| `modules/` | Per-module knowledge (auth, core-hr, leave, …) — domain rules, edge cases, why-decisions specific to that module | Persistent |
| `decisions/` | Architecture / design decision records (ADR-lite). One file per decision, dated, with status | Persistent |
| `handoffs/` | Short context drops between agents during a pipeline run (BA → dev → QA). Cleared periodically | Short-lived |
| `incidents/` | Bugs, production incidents, post-mortems. Link to the commit/PR that fixed | Persistent |

## Note conventions

- **Filenames**: `kebab-case.md`. For dated notes prefix with `YYYY-MM-DD-`.
- **Links**: Use Obsidian wiki links `[[note-name]]`. Agents should prefer wiki links over relative paths so backlinks work.
- **Tags**: Add `#module/auth`, `#agent/frontend-dev`, `#status/open` etc. at the top of the note for filterable views.
- **Frontmatter** (optional but recommended for structured notes):
  ```yaml
  ---
  type: decision | handoff | incident | module-note
  module: auth | core-hr | leave | ...
  status: draft | active | resolved | superseded
  created: 2026-05-19
  ---
  ```

## What does NOT go in the vault

- Secrets, tokens, passwords — use `.env` or `dotnet user-secrets`
- User stories — those live in [user-stories/](../../user-stories/)
- Test cases — those live in [test-cases/](../../test-cases/)
- Code — that's in [src/](../../src/)
- Generated logs or transient task state

The vault is for **knowledge that survives a conversation** — the *why* behind code, not the code itself.
