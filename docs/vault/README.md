---
type: index
description: Vault conventions and folder contract
---

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
- **Links**: Use Obsidian wiki links ``[[note-name]]``. Agents should prefer wiki links over relative paths so backlinks work.
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
- User stories — those live in [docs/BA/](../../docs/BA/)
- Test cases — those live in [docs/QA/](../../docs/QA/)
- Code — that's in [src/](../../src/)
- Generated logs or transient task state

The vault is for **knowledge that survives a conversation** — the *why* behind code, not the code itself.


## Open the vault at `docs/`, NOT `docs/vault/`

**Vault root = `docs/`.** Config lives in [`docs/.obsidian/`](../.obsidian/) and is committed
(except `workspace.json`, which is per-machine window layout and would churn every session).

Rooting at `docs/vault/` made a 34-note island next to the documentation that actually matters. With
the root at `docs/`, `[[US-PLT-005]]` resolves to the BA story, `[[TEST-FINDINGS#BUG-292]]` jumps to
the finding, and the BA stories + QA ledgers + ADRs + architecture become one graph. The archive
folders are excluded via `userIgnoreFilters` so they stay out of search and the graph.

## Linking rules (38 broken links came from getting these wrong)

**1. Wikilinks resolve by NOTE NAME, never by path.** `[[authentication-sso]]` works;
`[[../modules/authentication-sso]]` is permanently broken. Do not write a wikilink as if it were a
relative markdown link — that mistake accounted for 21 of the 38.

**2. A ledger ID is a HEADING, not a note.** `BUG-292` and `ISSUE-328` live inside
`docs/QA/TEST-FINDINGS.md`. Link them as `[[TEST-FINDINGS#BUG-292]]`. A bare `[[BUG-292]]` can never
resolve, because no such file exists.

**3. Targets outside `docs/` need a markdown link.** `CLAUDE.md` sits at the repo root, outside every
vault root — use `[CLAUDE.md](../../CLAUDE.md)`.

**4. Never wikilink another memory store.** See below — those names are not notes here.

**5. An unresolved link to a note that SHOULD exist is fine** — that is Obsidian's "create me"
affordance and a legitimate TODO. `Home.md` currently carries four: `[[auth]]`, `[[leave]]`,
`[[notifications-audit]]`, `[[training-benefits]]` — module notes nobody has written yet.

## Three memory stores — know which one you are in

| Store | Path | Scope | Tracked |
|---|---|---|---|
| **This vault** | `docs/vault/` | **Shared** — every agent + human | ✅ |
| **Per-agent working memory** | `.claude/agent-memory/{agent}/` | One agent, auto-loaded on its runs | ✅ *(since 2026-08-22)* |
| **Claude's own auto-memory** | `~/.claude/projects/…/memory/` | One human's Claude sessions, outside the repo | ❌ never |

Notes from the third store (`read-the-running-log`, `verify-code-not-ledger`, …) are **not vault
notes**. Referencing one as `[[read-the-running-log]]` creates a link that can never resolve; write
`` `memory:read-the-running-log` `` instead. Four such links existed before 2026-08-22.

**`.claude/agent-memory/` used to be gitignored**, which put 107 of the project's 141 notes (76%) on
one machine — unreviewed, unshared, one disk failure from gone — while this vault went from 70
commits in June to 5 in August. It is tracked now. The rule stands: **if it is worth sharing it goes
here; if it is one agent's operational note it goes in the private store** — but "private" no longer
means "invisible and unbacked-up".
