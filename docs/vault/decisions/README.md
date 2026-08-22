---
type: index
description: ADR-lite decision records
---

# Decisions

Architecture and design decision records, ADR-lite style.

## When to add a decision

Add a note here when the team picks one approach over another and that *why* matters later. Examples:
- "Why MediatR for application-layer dispatch"
- "Why tenant-per-schema vs tenant-per-row"
- "Why Angular standalone components over modules"

Skip trivial choices (variable names, file layout) — those are obvious from the code.

## Format

One file per decision: `YYYY-MM-DD-<short-slug>.md`. Use [[_template]] as a starting point.

## Index
*(add new decisions here as wiki links)*

- [[ADR-2026-08-21-headroom-not-adopted]] — Headroom context-compression proxy evaluated and rejected (fit, not credibility)
- [[ADR-2026-08-21-claude-mem-not-adopted]] — claude-mem auto-capture memory daemon evaluated and rejected; in-repo SubagentStop nudge adopted instead
