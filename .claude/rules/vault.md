---
paths:
  - "docs/vault/**"
---

# Vault rules (`docs/vault/` — shared agent memory)

**Open the Obsidian vault at `docs/`, not `docs/vault/`.** Config is committed in `docs/.obsidian/`.
Rooted at `docs/vault/` it is a 34-note island; rooted at `docs/` the BA stories, QA ledgers, ADRs and
architecture form one graph.

## Linking rules — 38 broken links came from getting these wrong

1. **Wikilinks resolve by NOTE NAME, never by path.** `[[authentication-sso]]` works;
   `[[../modules/authentication-sso]]` is permanently broken. 21 of the 38 were this mistake.
2. **A ledger ID is a HEADING, not a note.** Use `[[TEST-FINDINGS#BUG-292]]`, never `[[BUG-292]]`.
3. **Targets outside `docs/` need a markdown link** — `[CLAUDE.md](../../CLAUDE.md)`.
4. **Never wikilink another memory store** (see below).
5. **An unresolved link to a note that SHOULD exist is fine** — that is Obsidian's "create me"
   affordance and a legitimate TODO.

## Three memory stores — know which one you are in

| Store | Path | Scope | Tracked |
|---|---|---|---|
| **This vault** | `docs/vault/` | Shared — every agent + human | yes |
| **Per-agent memory** | `.claude/agent-memory/{agent}/` | One agent, auto-loaded on its runs | yes *(since 2026-08-22)* |
| **Claude auto-memory** | `~/.claude/projects/…/memory/` | One human's sessions, outside the repo | **never** |

Third-store names are not vault notes — write `` `memory:read-the-running-log` ``, not `[[…]]`.

**If it is worth sharing it goes here; if it is one agent's operational note it goes in the private
store.** Both are now tracked, so "private" no longer means "unbacked-up". Never duplicate a fact into
both. Secrets, generated logs and transient task state go in neither.
