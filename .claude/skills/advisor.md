---
name: advisor
description: "Technical-consultant advisory (REPORT-ONLY). Produces an evidence-anchored, ranked advisory over 3 net-new passes — tech-radar/dependency currency, ADR-drift, complexity/dead-code — plus light synthesis that LINKS (never re-runs) the existing auditors. Writes docs/Architecture/advisory-reports/, updates docs/Architecture/radar/tech-radar.md, proposes ADRs. Never edits src, deletes code, or bumps deps. Use for a periodic tech-health + decision-currency review."
user_invocable: true
---

# Technical Advisor (report-only)

A periodic **principal-engineer-grade advisory pass** over this codebase's tech health and
decision currency. It is **not a mega-agent** — v1 covers exactly 3 net-new passes (tech-radar/
dependency currency, ADR-drift, complexity/dead-code) and *synthesizes* the existing read-only
auditors (`/security-audit`, `/design-review`, `integration-enforcer`, `test-authenticator`,
`fault-diagnosis`-class findings) by **linking** to what they already measured, never by
re-running them. If you want a fresh security or design pass, run those skills directly — `/advisor`
tells you where they overlap with the new evidence, not the other way round.

All the actual measuring and judgment happens in `@principal-advisor` (`.claude/agents/review/
principal-advisor.md`) — a read-only agent that runs the 3 passes and returns one structured
advisory but writes nothing to disk. `/advisor` is the thin orchestrator around it: it parses the
invocation, delegates to the agent, and **persists** the agent's synthesis to the report/radar/ADR
artifacts described below. Nothing in this skill re-derives evidence the agent already produced —
keep the agent's conclusion, not its raw tool output, in the written artifacts.

## Usage

```
/advisor                        # all 3 passes, whole-repo scope
/advisor --radar                # tech-radar / dependency-currency pass only
/advisor --adr                  # ADR-drift pass only
/advisor --deadcode             # complexity / dead-code pass only
/advisor --module CHR           # scope any of the above to one module (e.g. Core HR)
/advisor --radar --module PAY   # flags compose: one pass, one module
```

With no flags, all 3 passes run against the whole repo. `--module {name}` scopes evidence
gathering (package manifests, ADR cross-references, code globs) to that module's slice of `src/`
and `docs/vault/modules/{module}.md` where applicable — pass selection and module scope are
independent and compose freely.

## Process

1. **Parse flags → pass set.** `--radar` / `--adr` / `--deadcode` are additive; any combination
   (or none, meaning "all 3") is valid. `--module {NAME}` narrows scope but never changes which
   passes run. Resolve `{scope}` for the output filename: the module name if `--module` was given,
   else `whole-repo`.
2. **Delegate to `@principal-advisor`** via the Agent tool, passing the resolved pass set and
   module scope in the prompt (e.g. "run passes: tech-radar, ADR-drift; scope: whole-repo"). Wait
   for its single structured advisory (the `PRINCIPAL ADVISORY` block — facts, ranked
   recommendations, tech-radar deltas, ADR-drift verdicts, gaps, and any `OUT-OF-LANE:` blocks).
   Keep that returned synthesis, not any intermediate tool output the agent may mention — the
   agent already did the evidence-gathering and ranking; this step does not re-run scanners or
   re-derive findings.
3. **Write `docs/Architecture/advisory-reports/{scope}-{YYYY-MM-DD}.md`** from the agent's synthesis, preserving its
   structure: verdict line, "What the tools measured" (facts, verbatim from the agent), "What I
   recommend" (the ranked, rated recommendations, verbatim), tech-radar deltas, ADR-drift verdicts,
   and gaps. Create the `docs/Architecture/advisory-reports/` directory if absent. This file is the durable evidence
   trail — do not summarize it away.
4. **Update `docs/Architecture/radar/tech-radar.md`** from the agent's "Tech-radar deltas" section: for each
   Adopt/Trial/Assess/Hold entry, add or move the row in the matching table (Languages &
   Frameworks / Platforms & Infra / Tools / Techniques), recording the ring, the movement (new /
   moved-from-X), and the fit-for-our-stack note + migration cost the agent supplied. Update the
   `_Last updated:_` line to today's date. Never move an entry to **Adopt** on the agent's say-so
   alone if the agent itself only proposed Assess/Hold for new tech — respect the agent's own
   conservatism.
5. **For ADR-drift findings, draft proposed ADR updates** in `docs/vault/decisions/` using the
   existing `_template.md` shape, `status: proposed` (never `accepted` — only a human flips that),
   for every ADR the agent classified as `drifted`, `drifted (planned-not-yet-implemented)`, or
   `stale`. Name the file `ADR-{today}-{slug}-drift-update.md`, cite the original ADR under
   **Links → Superseded by** (or a new "Amends" line if the template doesn't have one), and state
   in **Context** exactly what drifted (both the ADR side and the code side, as the agent cited
   it). A `current` or `superseded` verdict needs no new draft. These are proposals for human
   review, not accepted decisions — never edit the original ADR's `status:` field.
6. **Fold actionable items into `/auto-heal` + `docs/QA/TEST-FINDINGS.md`.** Any
   `OUT-OF-LANE:` block the agent emitted (a live bug, security exposure, broken test, missing
   wiring, decision-gated question) goes through the `/auto-heal` protocol exactly as any other
   sub-agent's out-of-lane flag would — filed, folded into the completion plan, re-prioritized.
   `/advisor`'s own ranked recommendations are advisory, not automatically filed as findings; only
   genuine out-of-lane discoveries (not restated advisory content) reach `TEST-FINDINGS.md`.
7. **Print a 3-line summary**: the verdict line, the count of recommendations by severity (e.g.
   "3 findings: 1 HIGH, 2 MED"), and the paths written (`docs/Architecture/advisory-reports/...`, `docs/Architecture/radar/
   tech-radar.md`, and any proposed ADR files).

## Report-only boundary

`/advisor` **writes only** to `docs/Architecture/advisory-reports/`, `docs/Architecture/radar/tech-radar.md`, and *proposed*
(`status: proposed`) ADR drafts in `docs/vault/decisions/`. It never edits `src/`, never deletes
code, never bumps a dependency version, and never wires a fitness test or CI gate. Dead-code
findings are **candidates for human confirmation** only — the complexity/dead-code pass reports
what looks unused after cross-checking against `integration-enforcer`'s wiring model; it does not
delete anything, and a candidate with any live-wiring doubt is reported at lower confidence rather
than dropped or acted on. An accepted ADR, a dependency bump, or a dead-code removal is always a
separate, human-decided follow-up step (e.g. via `/fix-finding` or `/implement-story`), never
something this skill or `@principal-advisor` performs itself.

## Relationship to other tooling

`/advisor` is the **forward-looking, decision-currency** counterpart to `/retro`'s
**backward-looking** engineering retrospective — `/retro` asks "what happened and how did we do,"
`/advisor` asks "is our tech stance and architecture still correct going forward." It does not
duplicate the existing read-only auditors — it **links and synthesizes** them:
- **`/security-audit`** — diff-scoped vulnerability review; `/advisor` cites its existing
  `docs/Architecture/security-reviews/*.md` output where a finding overlaps, never re-runs it.
- **`/design-review`** — visual/UX audit; same linkage via `docs/Design/design-reports/*.md`.
- **`integration-enforcer`** — wiring auditor; `/advisor`'s dead-code pass depends on it directly
  to avoid false positives on reflection/DI/routing-wired code.
- **`test-authenticator`** — test-quality auditor; informs confidence when `/advisor` cites test
  coverage as evidence.
- **`fault-diagnosis`** — root-cause method for live bugs; out-of-scope for `/advisor` (it advises
  on tech health, not individual incidents) but an `OUT-OF-LANE:` bug discovery routes toward it
  via `/auto-heal`.

Actionable, non-advisory discoveries feed **`/auto-heal`**, which files them into
`docs/QA/TEST-FINDINGS.md` and re-sorts the living completion plan — `/advisor` is an input to
that loop, not a replacement for it.

## Graceful degradation

If a pass's underlying tooling isn't wired yet (Knip not installed, CRAP/complexity tooling not
configured, a Roslyn dead-code analyzer not enabled), `@principal-advisor` runs whatever *is*
present for that pass and adds a line to the report's **Gaps** section naming the missing tool and
which pass it would have strengthened — it never fails the whole `/advisor` run over one missing
tool, and `/advisor` carries that Gaps section into the written report verbatim. These gaps are not
silently accepted forever: each one points at the relevant Wave in
`docs/DEV/TOOLING-ADOPTION-PLAN.md` (Wave 2/3 covers Knip, CRAP analysis, and the remaining static
analyzers) so a reader can see both "why this pass is thinner than it could be" and "where the plan
already says to fix that." A degraded pass still produces whatever evidence it can — it does not
block the other passes or suppress the whole advisory.
