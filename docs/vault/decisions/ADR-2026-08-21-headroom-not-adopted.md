---
type: decision
status: accepted
created: 2026-08-21
deciders: product owner + Claude (evaluation session)
tags: [tooling, agent-workflow, context-compression, rejected, adr-lite]
---

# Headroom (context-compression proxy) — evaluated, NOT adopted

## Context

[Headroom](https://github.com/headroomlabs-ai/headroom) was proposed as a "plugin" for this repo. It is
**not** a Claude Code plugin — there is no marketplace manifest and nothing lands in `.claude/`. It is a
local HTTP proxy + CLI that is placed *in front of* Claude Code by rewriting `~/.claude.json` / VS Code
settings (`headroom wrap vscode-claude`), plus a **user-scope** Serena MCP install. It compresses tool
output, logs, files and history before they reach the model, with reversible retrieval (`headroom_retrieve`).

The project is legitimate — Apache-2.0, ~67k stars, ~3.1k merged PRs since 2026-01, active daily, at
**v0.36.0**. The rejection is about **fit**, not credibility.

The problem it targets is real for us: long unattended `/implement-all` and `/test-all` loops burn context
and push against rate limits. So this decision should be revisited if that constraint gets worse.

## Decision

**Do not adopt Headroom** — not project-wide, not in the agent loops. Revisit only if weekly rate limits
become the binding constraint on the autonomous loops, and then only after it reaches a stable 1.x.

## Why rejected

1. **Wrong scope shape.** Everything else we adopt is project-scoped and travels with the repo
   (`.claude/settings.json`, `.mcp.json`). Headroom is a machine-wide interception layer that mutates
   global config and registers Serena at user scope, affecting every other project on the machine.
2. **The headline number is not our number.** Their own subtitle: 60–95% for *JSON data*, **15–20% for
   coding agents**. The 92% rows in the Proof table are code-search / log-dump workloads.
3. **C# is Tier 2 — "function body compression."** Tier 1 (full AST) is Python/JS/TS only. Our `src/` is
   ~2,651 `.cs` vs ~783 `.ts`. Our backend value — CQRS handlers, `TenantInterceptor`, the query filters in
   `AppDbContext.OnModelCreating` — lives in the *body*, not the signature. An auditor agent handed a
   stripped body either burns a retrieval round-trip or reasons over a stub.
4. **It compresses exactly what our discipline requires verbatim.** `/implement-all` remediation hands the
   *verbatim* errors to the owning dev agent; `@test-runner` correlates Serilog by `RequestId` to pull the
   real exception/stack/SQL; `/gap-analysis` and `@requirements-auditor` require `file:line` evidence.
   Lossy compression of logs and code is the failure class we have already been bitten by — see
   [[verify-code-not-ledger]] and the "read the running log" lesson.
5. **Reliability under unattended loops.** At v0.36.0 with ~506 open issues. A fix merged 2026-08-20:
   *"return 502, not 200, when upstream connect retries are exhausted"* — i.e. until then an exhausted
   upstream returned **HTTP 200**. Under an overnight `/loop /implement-all` that is silent garbage, not a
   visible failure.
6. **`HEADROOM_OUTPUT_SHAPER` effort-routing is hostile to our architecture.** It dials thinking effort
   *down* on turns where the model is resuming after a tool result — which is precisely the turn our
   auditors and remediation loop do their real reasoning on. (Off by default; would stay off.)
7. **We already bought the headroom elsewhere.** On Opus 5 with 1M context, context pressure is currently
   not the binding constraint.
8. **Auth surface.** `wrap vscode-claude` routes Claude Code authentication through a third-party process
   that holds the upstream token in memory. Local-only and open-source, so this is a "know what you're
   agreeing to" flag rather than a disqualifier — but it is a flag.

## Alternatives considered

- **Adopt fully (`headroom wrap vscode-claude` in the normal workflow)** — rejected: reasons 1–6 land
  directly on `/implement-all`, `/test-all`, and the read-only auditors, which are the core of the system.
- **Adopt for exploration only, never in the loops** — viable but rejected as not worth the global-config
  churn and the two-mode cognitive overhead for a 15–20% saving on a non-binding constraint.
- **Take `headroom learn` standalone** — the one genuinely low-risk piece: it mines failed sessions and
  writes corrections to `CLAUDE.local.md` (gitignored by default), with **no proxy and no interception**.
  Not adopted now, but this is the sanctioned entry point if we ever sample the ecosystem.

## Consequences

- No change to the current stack. Rate-limit / context pressure stays managed by the 1M-context model,
  native compaction, and the sub-agent delegation rule (Engineering Discipline #5) rather than by a proxy.
- If someone proposes Headroom again, this note is the answer — do not re-litigate without new evidence
  (a stable 1.x, Tier 1 C# support, or rate limits actually blocking the loops).
- **Trial protocol, if we ever do revisit:** throwaway session on a scratch repo, never on `/implement-all`
  or `/test-all`; wrap with `--code-memory none` to skip the user-scope Serena install; leave
  `HEADROOM_OUTPUT_SHAPER` off; verify `headroom unwrap vscode-claude` actually restores settings *before*
  trusting it.

## Links
- Upstream: https://github.com/headroomlabs-ai/headroom · docs: https://headroom-docs.vercel.app/docs
- Related: [[verify-code-not-ledger]], [[ADR-2026-07-29-tenant-secrets-are-platform-level]]
- Adoption context: `docs/DEV/TOOLING-ADOPTION-PLAN.md`
