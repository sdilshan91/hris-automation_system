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

## Finding schema (`TEST-FINDINGS.md`)
`BUG` broken vs spec · `ISSUE` contract/behavioural nit, drift, flaky · `ENH` improvement.
Severity `CRIT`/`HIGH`/`MED`/`LOW`. Status `OPEN` (set by the loop) → `WIP`/`FIXED`/`VERIFIED`/`WONTFIX`
(set by a human, never by the loop). Layer `FE`/`BE`/`DB`/`TEST`/`DATA`/`INFRA`. Always include root
cause + confidence, reproduction steps, and evidence.

## Report-only boundary
`/test-all`, `/test-us` and `@test-runner` **never fix code and never open PRs** — a failing test
produces a *finding*, not a fix attempt. Only `/verify-fix` may mark a finding RESOLVED.
