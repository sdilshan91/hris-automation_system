---
paths:
  - "docs/QA/**"
  - "docs/BA/**"
---

# Ledger rules (`docs/BA/` · `docs/QA/`)

## Standards and traceability
- User stories follow **IEEE 830** (`docs/BA/{module}/US-{MOD}-NNN.md`).
- Test cases follow **IEEE 829** (`docs/QA/{module}/TC-{MOD}-NNN.md`).
- **Every test case links back to a user story and a specific acceptance criterion.** IDs are
  cross-referenced from code too — `US-AUTH-007` appears in `TenantService` comments and in
  `docs/QA/authentication/`. Preserve these references when modifying related code.

## The ledgers are CLAIMS, not evidence
`STATUS.md`, `TEST-STATUS.md` and `TEST-FINDINGS.md` are **wrong in both directions** — 36+ documented
contradictions product-wide. Some rows claim work that does not exist; others claim open bugs that were
fixed. **The pessimistic direction costs most**: a story marked incomplete that actually shipped gets
rebuilt. Verify against `src/` before acting on a ledger row, and report a contradiction rather than
silently "correcting" it.

## The findings ledger is SPLIT (2026-09-01)

`TEST-FINDINGS.md` holds **live** findings (142); `TEST-FINDINGS-RESOLVED.md` holds **terminal**
ones (463). The working file went 1.9 MB → 422 KB — 85% of it was finished work every agent read past.

- **Next free ID: scan BOTH files.** `grep -hoE 'BUG-[0-9]+|ISSUE-[0-9]+|ENH-[0-9]+' docs/QA/TEST-FINDINGS*.md | sort -t- -k2 -n | tail -1` → +1.
  Scanning one file re-issues an ID another finding already owns.
- **De-dup: search BOTH.** A recurring defect re-opens or extends its ORIGINAL finding — usually archived.
- **Family rule:** every entry sharing an ID lives in the SAME file. A systemic finding (`BUG-003`)
  has `(EXTENDED to X)` / `NOTE` sub-entries; if any is live, the whole family stays in the working
  file. `LedgerTraceabilityTests` enforces this and that no live finding sits in the archive.
- Only `/verify-fix` moves an entry working → archive.

## Finding schema (`TEST-FINDINGS.md`)
`BUG` broken vs spec · `ISSUE` contract/behavioural nit, drift, flaky · `ENH` improvement.
Severity `CRIT`/`HIGH`/`MED`/`LOW`. One `- **Type / Severity / Status:**` line per entry (normalised 2026-09-01 from ten status spellings across four shapes). Live: `OPEN`/`DEFERRED`. Terminal: `RESOLVED`/`WONTFIX`/`RETRACTED`/`DUPLICATE`. Status `OPEN` (set by the loop) → `WIP`/`FIXED`/`VERIFIED`/`WONTFIX`
— `FIXED`/`VERIFIED` may be set by `/verify-fix` when the re-run TC evidence is green
(evidence-backed, not a judgment call); **`WONTFIX` stays human-only**, because an agent retiring its
own inconvenient finding is the failure mode this boundary exists to prevent. Layer `FE`/`BE`/`DB`/`TEST`/`DATA`/`INFRA`. Always include root
cause + confidence, reproduction steps, and evidence.

### Mandatory: SURVEY + AUDIT on every finding (global rule, 2026-09-07)

**No `BUG`/`ISSUE`/`ENH` is filed without both.** They go in the finding body — a scope or a
verification that exists only in a transcript did not happen.

- **SURVEY — how big is it?** State how many call sites / files / services / modules exhibit this,
  **and the unit you counted**. One instance or a class? A finding with no count cannot be sized,
  tiered, or de-duplicated, and its remedy cannot be scoped. `ISSUE-449` carried **three conflicting
  numbers** (276 / 369 / 93) for a single question purely because no source stated its unit.
- **AUDIT — is it true?** Every claim checked against `src/` with `file:line` evidence, **in both
  directions**: confirm the defect, *and* confirm the premise the finding asserts. Ledger rows are
  claims, not evidence (see above) — that applies to a finding's own text too.

**A finding already filed without them gets backfilled before it is scheduled for work.** Re-tiering
or re-scoping counts as scheduling: treat the stated premise as unverified and re-audit first.

**Why this is a hard rule and not advice** — measured on 2026-09-07:
- `ISSUE-150` asserted *"no security exposure today… LOW (traceability, not a live defect)"*.
  Auditing its three claims found **2 of 3 false** and uncovered `BUG-533`, a live permission bypass
  serving compensation to two personas the catalogue deliberately excludes.
- `ENH-018`'s own proposed remedy was a **false-green**: seeding bank data would have turned two TCs
  green while the feature stayed unreachable in production.
- `ISSUE-534` (**HIGH**) sat in a PR description and no ledger, invalidating every prior NFR-1 verdict.
- Of 28 findings filed that day — 7 HIGH — almost all were found by **looking**, not by a failing test.

## Report-only boundary
`/test-all`, `/test-us` and `@test-runner` **never fix code and never open PRs** — a failing test
produces a *finding*, not a fix attempt. Only `/verify-fix` may mark a finding RESOLVED.
