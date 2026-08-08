# HRM — Implemented vs Documented Gap Analysis (execution plan + live status)

> ## ✅ COMPLETE — 2026-08-08
> **All 17 passes run. ~674 verdicts. Deliverable: [`GAP-REGISTER.md`](GAP-REGISTER.md).**
>
> | | Planned | Actual |
> |---|---|---|
> | Passes | 17 (13 modules + B/C/D/E) | **17** ✔ |
> | Verdicts | ~550–570 | **~674** |
> | Module rows | 491 | 492 — **297 implemented (60%)**, 156 partial, 2 missing, 36 contradicted |
>
> **Headline: a product the ledger calls 99% done is ~60% done at AC level — and only 2 of 448 Must-Have ACs are MISSING.** The shortfall is reachability, not capability.
>
> **Deviations from plan, all recorded:**
> - `maxTurns: 60` was below the floor for AC-level work; two auditors returned nothing. Raised to 140 (`fd0b99ce`); five passes were recovered by resuming them to emit from existing context rather than re-running.
> - The Asset Management "first candidate gap" logged below was **wrong and is retracted** — see Pass B.
> - The pilot gate paid for itself: **8 of 12 auditors corrected the orchestrator's briefs**, and one orchestrator error reached a commit before being caught (`66c78be3`).


> **Started:** 2026-08-08 · **Tree:** `test/local-subdomains` (de-facto trunk, 810 ahead of stale `main`)
> **Driver:** [`/gap-analysis`](../../../.claude/skills/gap-analysis.md) · **Agent:** [`@requirements-auditor`](../../../.claude/agents/review/requirements-auditor.md)
> **Mode:** REPORT-ONLY. This effort produces a gap register. It fixes nothing, edits no `src/`, and
> corrects no status ledger. Fixing is a separate, human-decided cycle.
>
> **Living document** — the status table is updated as each pass completes.

---

## 1. The premise

`docs/BA/STATUS.md` claims **124 of 125 stories complete**. That ledger has been measured wrong in
**both directions**. `TEST-STATUS.md` and `TEST-FINDINGS.md` inherit the same defect.

> **A ledger line is a claim to be tested, not evidence.** Every verdict is anchored to `src/` with
> `file:line` proof. Where a ledger and the code disagree, the contradiction is itself a first-class
> finding (`CONTRADICTED`) — usually more valuable than the gap underneath it, because a false "done"
> is exactly what stops anyone from fixing the real thing.

**The pilot proved the premise on its first module.** `leave-management` is marked `[x]` on all 12
stories. Verified against code: **57% implemented, 40% partial, 2% missing.**

---

## 2. Scope (agreed 2026-08-08)

**Depth:** acceptance-criterion level for Must Have; story level for Should/Could.
**Evidence bar:** code exists **+** wired/reachable **+** a bound test exists. Test presence is
recorded, not executed. Leg 2 (wiring) explicitly includes the **FE/BE contract** — an Angular layer
reading a shape the API cannot emit fails, however good the backend is.
**Sources:** all four — functional (§5/§11 + 125 stories), NFR (§6), reverse pass, architecture (§8/§9/§10).

### Verdict taxonomy

| Verdict | Means |
|---|---|
| `IMPLEMENTED` | Code + wired + test-bound, with file:line proof |
| `PARTIAL` | Real code, requirement not fully met — the failing leg is named |
| `MISSING` | Nothing implements this |
| `UNVERIFIABLE` | Needs a running stack / load test / human judgement |
| `CONTRADICTED` | A ledger claims done; the code says otherwise. Outranks the others. |

---

## 3. Measured workload

| Module | Stories | Must | Should | Could | Must ACs | Verdict rows |
|---|---:|---:|---:|---:|---:|---:|
| admin-console | 12 | 12 | 0 | 0 | 72 | 72 |
| payroll | 13 | 11 | 2 | 0 | 60 | 62 |
| authentication | 16 | 8 | 8 | 0 | 51 | 59 |
| attendance | 11 | 8 | 3 | 0 | 43 | 46 |
| leave-management ✅ | 12 | 8 | 4 | 0 | 43 | 47 |
| recruitment | 10 | 8 | 2 | 0 | 39 | 41 |
| core-hr | 13 | 7 | 5 | 1 | 37 | 43 |
| notifications | 6 | 5 | 1 | 0 | 29 | 30 |
| onboarding | 6 | 4 | 2 | 0 | 21 | 23 |
| performance | 11 | 4 | 6 | 1 | 20 | 27 |
| reports | 5 | 4 | 1 | 0 | 20 | 21 |
| platform | 6 | 3 | 3 | 0 | 13 | 16 |
| training-benefits | 4 | 0 | 4 | 0 | 0 | 4 |
| **Total** | **125** | **82** | **41** | **2** | **448** | **491** |

Plus ~60–80 rows from passes B–E. **Expected total: ~550–570 evidence-anchored verdicts.**

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

---

## 5. Live status

Legend: `[ ]` pending · `[~]` running · `[x]` complete · `[!]` complete, quality concern · `[b]` blocked

### Setup
- [x] **S1** — Scope decisions agreed (depth · evidence bar · sources)
- [x] **S2** — `@requirements-auditor` authored → [`.claude/agents/review/requirements-auditor.md`](../../../.claude/agents/review/requirements-auditor.md)
- [x] **S3** — `/gap-analysis` authored → [`.claude/skills/gap-analysis.md`](../../../.claude/skills/gap-analysis.md)
- [x] **S4** — Workload measured, plan written
- [ ] **S5** — Register `/gap-analysis` + `@requirements-auditor` in the `CLAUDE.md` skill/agent tables
- [ ] **S6** — Restart the Claude Code session so `@requirements-auditor` registers as a real subagent type *(agent definitions load at startup; the pilot ran via `general-purpose` reading the contract file, which worked but is a workaround)*

### Pass A — functional forward (per module)
- [x] **A0 · PILOT — leave-management** → [`pass-a-leave-management.md`](pass-a-leave-management.md) · **27 IMPLEMENTED / 19 PARTIAL / 1 MISSING**
- [x] **A-gate** — 4 of 4 orchestrator spot-checks confirmed; auditor also corrected two errors in its own brief. **Calibration accepted, cleared for fan-out.**
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
- [ ] **B** — doc→story coverage *(Asset Management already a confirmed candidate gap)*
- [ ] **C** — NFR §6.1–6.12
- [ ] **D** — reverse: code with no doc/story
- [ ] **E** — architecture conformance §8/§9/§10

### Pass F — synthesis
- [ ] **F1** — `GAP-REGISTER.md`: stable `GAP-###` IDs, ranked Must+CONTRADICTED first
- [ ] **F2** — Headline rollup: *claimed done vs verified done*, per MoSCoW tier
- [ ] **F3** — Contradiction report: every ledger line the code refutes, both directions
- [ ] **F4** — Hand-off to [`COMPLETION-PLAN.md`](../../QA/plans/COMPLETION-PLAN.md) + `TEST-FINDINGS.md` via [`/auto-heal`](../../../.claude/skills/auto-heal.md)

---

## 6. Sequencing

| Wave | Contents | Gate |
|---|---|---|
| 0 ✅ | A0 pilot | **Passed** — verdict quality hand-validated before spending 16 more agents |
| 1 | A1–A4 + B + C + D + E (8 agents) | — |
| 2 | A5–A8 (4 agents) | — |
| 3 | A9–A12 (4 agents) | — |
| 4 | F synthesis (orchestrator) | — |

---

## 7. What the pilot changed

The pilot did more than validate the method — it found the **dominant gap class in this codebase**,
and the fan-out briefs were updated accordingly:

> Of 15 PARTIAL verdicts in leave-management, **10 failed on wiring or the frontend, not the backend.**
> The .NET layer is strong. The Angular layer breaks — and its Karma specs do not catch it **because
> they mock response shapes the API cannot produce.** Four separate leave FE suites are green against
> impossible payloads.

This is the mechanism by which `STATUS.md` became wrong: a story is marked done when BE tests and FE
tests are both green, and nobody checks that the two halves can actually talk. **Every subsequent
module audit now explicitly compares the backend DTO to the TypeScript interface field by field.**

---

## 8. Known risks

| Risk | Mitigation |
|---|---|
| **Concurrent session shares this working tree** | See §9. Artifacts backed up outside the repo; commit-as-you-go for later waves. |
| Auditors rubber-stamp | Mandatory file:line; pilot gate passed 4/4 |
| Auditors manufacture gaps | Agent told naming drift ≠ gap; confidence % required; pilot pushed back on its own brief, as designed |
| Vocabulary mismatch → false MISSING | ≥3 naming variants + feature folder before declaring MISSING |
| 448 AC verdicts exceed one context | Per-module agents; only conclusions return, raw output persisted |
| Result becomes a third stale ledger | Dated snapshot; hands off to the single living `COMPLETION-PLAN` |

---

## 9. ⚠ Incident — shared working tree (2026-08-08 01:29)

A **second Claude Code session** (PID 3080262, started 00:44) is operating on this same working tree.
Its activity, from `git reflog`:

```
01:22:55  checkout: docs/close-issue-361 → test/local-subdomains
01:22:56  pull -q: Fast-forward
01:24:53  checkout + commit: docs(BUG-291): close it
01:29:46  checkout: docs/close-bug-291 → test/local-subdomains
```

**Impact:** the branch changed underneath this session (from `docs/close-issue-361` to
`test/local-subdomains`), and the first copies of the agent, skill, and plan files — written at 01:29
and never committed — were lost. They have been re-created.

**Mitigations now in force:**
- Artifacts mirrored outside the repo (session scratchpad) immediately after each write.
- `@requirements-auditor` is forbidden from running any mutating git command (`checkout`, `stash`,
  `clean`, `pull`, `commit`, `restore`) — a shared tree makes those destructive to other sessions.
- `/gap-analysis` step 1 now requires a shared-tree check before fan-out.
- Later waves should commit pass outputs as they land rather than holding them untracked.

**Open decision for the human:** whether to pause the other session, or accept the risk and commit
frequently. Do not start an 8-agent wave into a tree another process may `checkout` mid-run.

---

## 10. Changelog

| Date | Event |
|---|---|
| 2026-08-08 | Plan created. Scope agreed (all four sources; AC-level for Must). Agent + skill authored. Workload measured: 125 stories / 448 Must ACs / ~550 verdicts / 17 agents. First candidate gap logged (Asset Management, documented §5.1/§11.12, zero story + zero code). |
| 2026-08-08 | **Pilot A0 complete and validated 4/4.** leave-management: 27 IMPLEMENTED / 19 PARTIAL / 1 MISSING against a ledger claiming 12/12 done. Dominant gap class identified (FE/BE contract mismatch masked by green specs mocking impossible shapes); fan-out briefs updated. Shared-working-tree incident logged (§9) — fan-out held pending a human decision. |
