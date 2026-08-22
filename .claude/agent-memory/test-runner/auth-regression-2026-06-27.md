---
name: auth-regression-2026-06-27
description: REPORT-ONLY regression re-test of all 9 tracked Authentication stories 2026-06-27 — every prior finding STILL PRESENT, zero new/regressed; one improvement delta (reset now enforces min-len 12)
metadata:
  node_type: memory
  type: project
  originSessionId: ca3ccad7-eb1c-4117-9455-4a5bc7487c05
---

REPORT-ONLY **regression** pass of the 9 tracked Authentication stories (US-AUTH-001/002/003/004/006/007/008/009/010 — MFA-005 NOT a tracked ledger line, left out of scope) on **2026-06-27** after PRs #110/#111/#112. Re-executed the key representative TCs per story; appended regression-delta notes to the 9 TEST-STATUS.md lines + to BUG-003 and BUG-040 in TEST-FINDINGS.md. **No new findings opened; all stories stay `[!]`; Tally unchanged.**

**Verdict: every prior auth finding STILL PRESENT, unchanged. Zero new/regressed defects from #110/#111/#112.**
- **BUG-040 CRIT still present** — bogus-token reset (`token:"totally-bogus-..."`) → 200 takeover, re-confirmed on throwaway qa-lockout-2. **DELTA (improvement):** reset path now ENFORCES password min-length ≥12 (9-char `Admin@123!` reset → 400 "must be at least 12 characters") — partial fix on the BUG-004 pw-policy front.
- **BUG-003 CRIT still present at root locus US-AUTH-007/TenantResolutionMiddleware** — acme JWT + `X-Tenant-Subdomain: techoneglobal` → `GET /tenant/users` returns techoneglobal's `sachithra@techoneglobal.org` (count 1), not acme's 8 users. READ-ONLY probe per the 2026-06-27 safety policy (no cross-tenant writes). TC-AUTH-054 still FAIL.
- BUG-039/041/042/043/044/045/046 all still present by reference (code paths unchanged); BUG-044 re-confirmed live (5th/threshold attempt returns generic msg, lockout text one request late).
- Auth CORE still SOLID & unchanged: login JWT+refresh-cookie+generic-401-no-enum; refresh rotation+reuse-detection; logout+cookie-clear; RBAC no-escalation (employee/HR 403); switch membership-validation (self-protected); session list/revoke IDOR-safe; sequential lockout at 5.
- **#112 SSO is cleanly namespaced** (`/api/v1/auth/sso/{challenge,callback}`, additive MFA fields on login response mfaChallenge/mfaMethod/mfaEnrollmentRequired) — no interference with local auth. Serilog today: 0 ERR/FTL, no 5xx during run.

**Throwaway-account residue (documented):** BUG-040 probe could NOT restore qa-lockout-2 to the 9-char shared `Admin@123!` (now policy-rejected ≥12) → left on **`Admin@123456!`**. qa-lockout-1 unlocked + still on **`N3wS3cure!Pass2026`** (from a prior run). Shared personas (tenantadmin/hr/manager/employee@acme.test) untouched, all verified `Admin@123!`→200. acme tenant id `019ef3ba-ffb7-7eec-b24f-7ad806ca1cb9`; foreign isolation tenant = **techoneglobal** (resolves; user `sachithra@techoneglobal.org`). See [[auth-full-test-pass-2026-06-25]], [[qa-personas-reseed-2026-06-25]], [[testing-loop-report-only]].
