---
type: decision
date: 2026-08-22
status: accepted
tags: [agents, claude-code, cost, setup-drift]
---

# Unpin agent models — let sub-agents inherit the session model

## Context

Nine of the ten agent definitions under [.claude/agents/](../../../.claude/agents/) carried
`model: claude-opus-4-8` in their frontmatter:

`backend-dev`, `frontend-dev`, `qa-engineer`, `business-analyst`, `browser-debugger`,
`test-runner`, `test-authenticator`, `integration-enforcer`, `principal-advisor`.

The tenth, `requirements-auditor`, carried **no** pin and silently inherited.

Two problems, found by a setup scan on 2026-08-22:

1. **Capability inversion.** The orchestrating session runs Opus 5. Every pinned agent ran a
   generation behind it. The agent that *delegates* was stronger than every agent that
   *implements, reviews, and audits* — which is backwards: delegation is the cheap step and
   implementation is where model quality actually converts into correctness. `/implement-all`
   runs its whole verify-and-remediate loop on the sub-agents, not on the orchestrator.

2. **It was drift, not a decision.** No rationale existed anywhere — not in `.claude/`, not in
   `docs/vault/decisions/`, not in a comment. A pin with no recorded reason is indistinguishable
   from a value that was right once and never revisited, and the `requirements-auditor` omission
   proves nobody was maintaining the set as a set: `/gap-analysis` ran on a different model than
   `/advisor` with nothing saying that was intended.

## Decision

**Remove the `model:` key from all agent definitions.** All ten now inherit the session model, so
whatever the human picks for the session applies uniformly to everything that session spawns.

## Consequences

- Sub-agent capability now tracks the session, and upgrading is a single `/model` choice rather
  than ten file edits that will drift again.
- **Cost follows the session model.** If a cheaper tier is wanted, that is now a session-level
  choice. It was never a real saving before anyway — the *expensive* work (long implementation
  runs, the 3-attempt remediation loop) sat on the pinned agents, so the pin was applying the
  discount to the highest-volume consumers.
- **If a per-role split is wanted later, pin deliberately and record it here.** The defensible
  version is by role — a cheap tier for mechanical agents, the top tier for the read-only
  auditors whose whole value is judgement — not one ID copy-pasted nine times.
- **Inert until the session restarts.** Agent definitions load at startup; see
  [[claude-agents-load-at-startup]].

## Why this is written down

The failure mode being avoided is the one that produced the pin: a setup fact that was true when
someone typed it and false for a long time afterwards, with nothing recording the intent. The same
scan found `dotnet-skills` declared, documented at length, and never installed, and CLAUDE.md
claiming there was no backend test project while `HRM.Tests` held 575 files. Configuration that
lives outside `src/` is outside the CI gates, so it drifts silently — this note and `/retro`'s
setup-drift pass are the counterweight.
