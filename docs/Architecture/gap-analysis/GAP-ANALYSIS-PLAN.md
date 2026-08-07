# HRM — Implemented vs Documented Gap Analysis (execution plan + live status)

> **Started:** 2026-08-08 · **Base branch:** `test/local-subdomains` (de-facto trunk, 809 ahead of `main`)
> **Driver:** [`/gap-analysis`](../../../.claude/skills/gap-analysis.md) · **Agent:** [`@requirements-auditor`](../../../.claude/agents/review/requirements-auditor.md)
> **Mode:** REPORT-ONLY. This effort produces a gap register. It fixes nothing, edits no `src/`, and
> corrects no status ledger. Fixing is a separate, human-decided cycle.
>
> **Living document** — the status table below is updated as each pass completes.

---

## 1. The premise

`docs/BA/STATUS.md` claims **124 of 125 stories complete**. That ledger has been measured wrong in
**both directions** — stories marked done that were never built, and stories marked pending that
shipped long ago. `TEST-STATUS.md` and `TEST-FINDINGS.md` inherit the same defect.

So the governing rule for this entire exercise:

> **A ledger line is a claim to be tested, not evidence.** Every verdict is anchored to `src/` with
> `file:line` proof. Where a ledger and the code disagree, the contradiction is itself a first-class
> finding (`CONTRADICTED`) — usually more valuable than the gap underneath it, because a false "done"
> is exactly what stops anyone from fixing the real thing.

An early confirmation, found in the first minutes of scoping: the tech doc specifies an
**Asset Management (lite)** module in §5.1 and §11.12. There is no `docs/BA/asset-management/`, no
backend `Features/Assets`, and no frontend feature. A whole documented module with zero story
coverage — invisible to a story-level ledger by construction. Logged as the first candidate gap.

---

## 2. Scope (agreed 2026-08-08)

**Depth:** acceptance-criterion level for Must Have; story level for Should/Could.
**Evidence bar:** code exists **+** wired/reachable **+** a bound test exists. Test presence is
recorded, not executed (execution is `/test-all`'s job).
**Sources in scope:** all four — functional (§5/§11 + 125 stories), NFR (§6), reverse pass
(code with no doc), architecture conformance (§8/§9/§10).

### Verdict taxonomy

| Verdict | Means |
|---|---|
| `IMPLEMENTED` | Code + wired + test-bound, with file:line proof |
| `PARTIAL` | Real code, but the requirement is not fully met — the failing leg is named |
| `MISSING` | Nothing implements this |
| `UNVERIFIABLE` | Needs a running stack / load test / human judgement to settle |
| `CONTRADICTED` | A ledger claims done; the code says otherwise. Outranks the others. |

---

## 3. Measured workload

| Module | Stories | Must | Should | Could | Must ACs | Verdict rows |
|---|---:|---:|---:|---:|---:|---:|
| admin-console | 12 | 12 | 0 | 0 | 72 | 72 |
| payroll | 13 | 11 | 2 | 0 | 60 | 62 |
| authentication | 16 | 8 | 8 | 0 | 51 | 59 |
| attendance | 11 | 8 | 3 | 0 | 43 | 46 |
| leave-management | 12 | 8 | 4 | 0 | 43 | 47 |
| recruitment | 10 | 8 | 2 | 0 | 39 | 41 |
| core-hr | 13 | 7 | 5 | 1 | 37 | 43 |
| notifications | 6 | 5 | 1 | 0 | 29 | 30 |
| onboarding | 6 | 4 | 2 | 0 | 21 | 23 |
| performance | 11 | 4 | 6 | 1 | 20 | 27 |
| reports | 5 | 4 | 1 | 0 | 20 | 21 |
| platform | 6 | 3 | 3 | 0 | 13 | 16 |
| training-benefits | 4 | 0 | 4 | 0 | 0 | 4 |
| **Total** | **125** | **82** | **41** | **2** | **448** | **491** |

Plus roughly 60–80 further verdict rows from passes B–E (NFR bullets, architecture conformance
points, doc-coverage and reverse-pass findings). **Expected total: ~550–570 evidence-anchored verdicts.**

---

## 4. Passes

| Pass | Question | Scope | Agents |
|---|---|---|---:|
| **A** | Do the stories' ACs exist in code? | 125 stories / 13 modules | 13 |
| **B** | Does the tech doc promise things no story covers? | §3.1, §5.1, §5.2, §11.1–11.14 vs `BA/INDEX.md` | 1 |
| **C** | Are the §6 non-functional requirements satisfied? | §6.1–6.12 | 1 |
| **D** | Is there shipped code no document describes? | 36 BE features + 15 FE features | 1 |
| **E** | Does the build match the documented architecture? | §8 pipeline, §9 tenancy, §10 layout | 1 |
| **F** | What is the ranked register? | orchestrator synthesis | 0 |

**17 agents total.** Passes B–E run concurrently with Pass A wave 1.

---

## 5. Live status

Legend: `[ ]` pending · `[~]` running · `[x]` complete · `[!]` complete, quality concern · `[b]` blocked

### Setup
- [x] **S1** — Scope decisions agreed (depth · evidence bar · sources)
- [x] **S2** — `@requirements-auditor` agent authored → [`.claude/agents/review/requirements-auditor.md`](../../../.claude/agents/review/requirements-auditor.md)
- [x] **S3** — `/gap-analysis` skill authored → [`.claude/skills/gap-analysis.md`](../../../.claude/skills/gap-analysis.md)
- [x] **S4** — Workload measured, this plan written
- [ ] **S5** — Register `/gap-analysis` + `@requirements-auditor` in `CLAUDE.md` tables *(deferred to after the pilot proves the design)*

### Pass A — functional forward (per module)
- [ ] **A0 · PILOT — leave-management** *(8 Must / 43 ACs; chosen because its defect history — BUG-291, ISSUE-197, ISSUE-284 — gives independently checkable ground truth)*
- [ ] **A-gate** — orchestrator spot-checks 3 pilot verdicts by opening the cited files; tune the agent prompt before any fan-out
- [ ] **A1** — admin-console (72 ACs)
- [ ] **A2** — payroll (60 ACs)
- [ ] **A3** — authentication (51 ACs)
- [ ] **A4** — core-hr (37 ACs)
- [ ] **A5** — recruitment (39 ACs)
- [ ] **A6** — attendance (43 ACs)
- [ ] **A7** — notifications (29 ACs)
- [ ] **A8** — onboarding (21 ACs)
- [ ] **A9** — performance (20 ACs)
- [ ] **A10** — platform (13 ACs)
- [ ] **A11** — reports (20 ACs)
- [ ] **A12** — training-benefits (4 stories, story-level only)

### Passes B–E
- [ ] **B** — doc→story coverage *(Asset Management already a confirmed candidate)*
- [ ] **C** — NFR §6.1–6.12
- [ ] **D** — reverse: code with no doc/story
- [ ] **E** — architecture conformance §8/§9/§10

### Pass F — synthesis
- [ ] **F1** — `GAP-REGISTER.md`: every gap assigned a stable `GAP-###`, ranked Must+CONTRADICTED first
- [ ] **F2** — Headline rollup: *claimed done vs verified done*, per MoSCoW tier
- [ ] **F3** — Contradiction report: every ledger line the code refutes
- [ ] **F4** — Hand-off — fold actionable gaps into [`COMPLETION-PLAN.md`](../../QA/plans/COMPLETION-PLAN.md) + session TODO via [`/auto-heal`](../../../.claude/skills/auto-heal.md); file genuine defects to `TEST-FINDINGS.md`

---

## 6. Sequencing

| Wave | Contents | Gate |
|---|---|---|
| 0 | A0 pilot (1 agent) | **Hard gate.** Verdict quality validated by hand before spending 16 more agents. |
| 1 | A1–A4 + B + C + D + E (8 agents) | Highest-Must-density modules alongside all four doc passes |
| 2 | A5–A8 (4 agents) | — |
| 3 | A9–A12 (4 agents) | — |
| 4 | F synthesis (orchestrator) | — |

**Why the pilot gate is non-negotiable:** fanning out 17 unvalidated auditors would just produce a
second wrong ledger, faster and more expensively than the first one. One module, checked by hand,
buys the right to trust the other twelve.

---

## 7. Hard boundaries

- Never edits `src/`.
- Never edits `docs/BA/STATUS.md` or `docs/QA/TEST-STATUS.md` — a false ledger line gets *reported*,
  not silently corrected. Closing findings is `/verify-fix`'s exclusive authority.
- Never opens a PR, never runs a migration, never starts the stack.
- Never lets a document promote a verdict to IMPLEMENTED.
- No silent caps — anything sampled rather than covered is declared as sampled.

---

## 8. Known risks

| Risk | Mitigation |
|---|---|
| Auditors rubber-stamp (`IMPLEMENTED`, vague evidence) | Mandatory file:line; pilot gate with hand spot-checks |
| Auditors manufacture gaps to look thorough | Agent is explicitly told naming drift ≠ gap; confidence % required on uncertain verdicts |
| Vocabulary mismatch (doc noun ≠ code noun) causes false MISSING | Agent must try ≥3 naming variants + the feature folder before declaring MISSING |
| 448 AC verdicts exceed one context | Per-module agents; only conclusions return to the orchestrator, raw output persisted to disk |
| Result becomes a third stale ledger | Register is a dated snapshot with a commit SHA, and hands off to the single living `COMPLETION-PLAN` |

---

## 9. Changelog

| Date | Event |
|---|---|
| 2026-08-08 | Plan created. Scope agreed. Agent + skill authored. Workload measured: 125 stories / 448 Must ACs / ~550 total verdicts across 17 agents. First candidate gap logged (Asset Management module, documented in §5.1/§11.12, zero story + zero code coverage). |
