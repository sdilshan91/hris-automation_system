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

## Report-only boundary
`/test-all`, `/test-us` and `@test-runner` **never fix code and never open PRs** — a failing test
produces a *finding*, not a fix attempt. Only `/verify-fix` may mark a finding RESOLVED.
