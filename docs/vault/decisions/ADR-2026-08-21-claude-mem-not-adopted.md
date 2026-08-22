---
type: decision
status: accepted
created: 2026-08-21
deciders: product owner + Claude (evaluation session)
tags: [tooling, agent-workflow, memory, rejected, adr-lite]
---

# claude-mem (auto-capture memory daemon) — evaluated, NOT adopted

## Context

[claude-mem](https://github.com/thedotmack/claude-mem) was proposed as a plugin for this repo. Unlike
[[ADR-2026-08-21-headroom-not-adopted]] it *is* a real Claude Code plugin (`/plugin install claude-mem`,
or `npx claude-mem install`). It installs **5 lifecycle hooks** (SessionStart, UserPromptSubmit,
PostToolUse, Stop, SessionEnd) feeding a local **Bun HTTP worker** that LLM-compresses tool-use
"observations" into **SQLite + a Chroma vector DB**, exposed back through **4 MCP tools** and a
`mem-search` skill. Extra runtime deps: Node 20+, Bun, `uv`, Chroma.

The project is credible — Apache-2.0, ~91.4k stars, ~8.0k forks, ~2,400 commits since 2025-08-31, v13.15.3
shipped 2026-08-20. As with Headroom, the rejection is about **fit**, not credibility.

**The problem it targets is real and measured.** CLAUDE.md's agent contract asks every agent to record
non-obvious decisions in `docs/vault/`. That is a discipline contract, and discipline contracts leak.
Replaying the 120 subagent transcripts on disk (`<session>/subagents/agent-*.jsonl` + its `.meta.json`
sidecar), the four *writing* agents — `backend-dev`, `frontend-dev`, `qa-engineer`, `business-analyst` —
recorded a vault/agent-memory note on only **11 of 27 substantive runs (a 59% leak)**. Counting empty
`.claude/agent-memory/` dirs *overstates* the problem, since most empties are read-only auditors that
correctly never write.

## Decision

**Do not adopt claude-mem.** Instead, close the measured gap in-repo with
`.claude/hooks/scripts/vault-compliance-advisor.py` — a non-blocking `SubagentStop` advisory that flags a
writing agent which changed ≥3 files under `src/` | `docs/BA/` | `docs/QA/` but wrote nothing to
`docs/vault/` or `.claude/agent-memory/`. Revisit only if the nudge demonstrably fails to move the leak
rate, and then re-measure with the same method before adopting anything.

## Why rejected

1. **A fourth, opaque memory store in a markdown-in-git architecture.** We already run three:
   `docs/vault/` (shared, git-tracked, human-browsable), `.claude/agent-memory/` (per-agent, gitignored),
   and the harness's own store. CLAUDE.md explicitly warns against fragmenting them. claude-mem adds
   SQLite + vectors — not reviewable in a PR, not in git, does not travel with the repo. Every other
   knowledge surface here is markdown a human can read.
2. **It spends subscription quota on every session.** The compression step is an LLM call. Upstream #3037:
   *"infinite poison/respawn loop when the subscription account hits its weekly/usage limit."* We run
   `/implement-all` and `/test-all` under `/loop` unattended for hours; every turn feeds the compressor,
   and that failure lands mid-loop. Local + MCP mode was explicitly designed to need **no API credits**.
3. **Hook budget collision.** We already run 7 `PreToolUse` + 2 `PostToolUse` + `Stop`/`Notification`/
   `PermissionRequest`/`SubagentStop` hooks at 5s timeouts. This adds 5 more, including `PostToolUse` on
   *everything*. Upstream issue filed 2026-08-20: every hook invocation re-runs the worker boot path,
   ~460 MB/day of redundant reads. Our loops are exactly that tool-call-dense workload. See also #902
   (orphaned subprocesses under heavy tool use) and #761 (Chroma RAM / zombie processes).
4. **Silent-failure class we have already been bitten by.** The top open issue at evaluation time is
   *"observations and session_summaries silently stopped being generated (since 2026-05-28)."* A memory
   system that silently stops remembering is worse than none, because the team stops writing to the vault
   while trusting it. Same lesson as `memory:read-the-running-log` and `memory:verify-code-not-ledger`.
5. **Unscanned durable copy of tenant data.** `secret-guard` fences secrets at Write/Edit time in `src/`.
   claude-mem persists *tool output* — `@test-runner` curl probes carrying JWTs, EF SQL results, tenant
   rows — to `~/.claude-mem`, outside secret-guard and outside our gitignore discipline. Local-only, so a
   flag rather than a breach; we did not verify whether it redacts. Confidence ~80%.
6. **Subagent capture is where all our value lives, and it is unsettled.** Knowledge here is generated
   *inside* `@backend-dev` / `@frontend-dev` / `@qa-engineer`, not the orchestrator. Upstream shipped a fix
   this week for hooks not firing on subagent (`thread_spawn`) runs — on the **Codex** adapter. We did
   **not** confirm the Claude Code path has the same hole, so this is an open question, not a proven
   defect. But it is the single question that decides whether the tool is useful to us at all.

## Alternatives considered

- **Adopt project-scoped** — rejected: reasons 1–3 land directly on the unattended loops, which are the
  core of the system.
- **Adopt user-scoped for exploration only** — the sanctioned way to sample it if we ever revisit: install
  user-scoped (never project-scoped), run exactly one `/implement-all` story on a throwaway branch, then
  check (a) whether subagent work appears in observations at all and (b) the token delta. If subagent
  capture comes back empty, it is dead on arrival here.
- **Build the nudge in-repo (chosen)** — the gap is *compliance*, not capability. A `SubagentStop` hook
  turns the contract into a signal the same way `test-integrity-guard` turns "never weaken a test" into a
  guard: no new service, no vector DB, no LLM spend, and everything it encourages stays as reviewable
  markdown in git. Advisory rather than blocking, because a hard block would wedge the unattended loops
  (the exact failure we rejected above) and would manufacture vault noise on runs that genuinely learned
  nothing.

## Consequences

- No new runtime services, daemons, or databases. Shared memory stays `docs/vault/` + `.claude/agent-memory/`.
- `vault-compliance-advisor` ships registered in `.claude/settings.json` under `SubagentStop`. It is
  advisory by default; `CLAUDE_VAULT_ENFORCE=1` escalates to blocking, `CLAUDE_VAULT_MIN_WRITES` tunes the
  threshold, `CLAUDE_DISABLE_VAULT_ADVISOR=1` silences it. It logs to `.claude/hooks/vault-compliance.log`
  (gitignored) so overnight loops leave a reviewable trail.
- **Re-measure before re-litigating.** The 59% leak rate is reproducible from the subagent transcripts; if
  someone proposes an auto-capture memory tool again, run that measurement first and show the nudge failed.

## Links
- Upstream: https://github.com/thedotmack/claude-mem
- Sibling rejection: [[ADR-2026-08-21-headroom-not-adopted]]
- Related: `memory:verify-code-not-ledger`, `memory:read-the-running-log`
- Hook: `.claude/hooks/scripts/vault-compliance-advisor.py` · contract: CLAUDE.md "Shared Memory"
