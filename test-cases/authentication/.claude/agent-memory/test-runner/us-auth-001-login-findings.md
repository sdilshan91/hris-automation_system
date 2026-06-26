---
name: us-auth-001-login-findings
description: US-AUTH-001 admin/user login test pass (2026-06-25) — 4 PASS / 1 FAIL, 2 new ISSUEs; login flow facts (audit, isolation, timing, lockout)
metadata:
  type: project
---

# US-AUTH-001 login test pass (2026-06-25)

REPORT-ONLY API run, 5 owned TCs (TC-AUTH-001/002/003/004 single-owned + TC-AUTH-ISO-001 dual-owned US-AUTH-001+007). Route `POST /api/v1/auth/login`, tenant-aware via `X-Tenant-Subdomain`. **4 PASS, 1 FAIL (ISO).**

**Why:** capture login-flow behavior so future auth/session runs skip rediscovery.
**How to apply:** reuse for US-AUTH-002/005/007/009/010 runs and any re-test of these findings.

## Verdicts
- TC-AUTH-001 PASS — 200; JWT carries sub/email/tenant_id/user_tenant_id/roles/permissions/nbf/exp (RS256, ~15min); refresh cookie `httponly; secure; samesite=strict; path=/api/v1/auth` ~7d; refreshToken nulled in body. UI redirect/branding steps fe-platform-bound but core contract API-verified.
- TC-AUTH-002 PASS — 401 generic "Invalid email or password.", no token/cookie, FailedLoginCount++ (reset on next success), `login_failure` audited.
- TC-AUTH-003 PASS — identical 401, comparable timing (dummy BCrypt.Verify in AuthService.cs:72 for non-existent), no enumeration. Non-existent user is NOT audited (no user row to attribute) but IS Serilog-WRN'd.
- TC-AUTH-004 PASS — 400 with field-specific FluentValidation errors (empty/format/>150). UI client-side arms fe-platform-bound.
- TC-AUTH-ISO-001 FAIL — core isolation PASSES (acme user→platform=403, system user→acme=403, both "no active membership"; unknown subdomain=404 static "Workspace not found" no SPA; happy-path token tenant_id=acme only; acme token→system endpoint=403). FAILS only on step 7 → ISSUE-049.

## New findings
- **ISSUE-048 (MED)** — NO `login_success` audit row. `IssueTokensAsync` (AuthService.cs:1547-1639, single success exit for pw/MFA/SSO/switch) only Serilog-logs success (line 1629); `login_failure` IS audited (lines 120,982). FR-9/AC-1-step11 wants success too. Live: `GET /tenant/audit-logs?action=login_success` → 0 rows ever; `?action=login_failure` → rows present.
- **ISSUE-049 (LOW)** — refresh accepted cross-subdomain. `RefreshTokenAsync` finds token by hash globally (AuthService.cs:289-292), never checks `_tenantContext.TenantId == storedToken.TenantId`. acme cookie @platform → 200 BUT minted token tenant_id=acme (NOT platform) → no cross-tenant escalation, just spec deviation (ISO step7 wants 401). Reuse/rotation works.

## Operational facts (carry forward)
- Login order: global user lookup → lock check → IsActive → BCrypt → **tenant resolve + membership** (password verified BEFORE membership; cross-tenant valid-creds attempt still 403, not 200).
- Audit goes to `audit_logs` table (NOT Serilog file). Filter via `?action=login_failure` (the `?eventType=` param is IGNORED — returns all rows). `tenantadmin@acme.test` holds `Audit.View`.
- Lockout default threshold 5; a successful login resets FailedLoginCount=0. Did 1-3 wrong attempts on tenantadmin/manager then restored with valid login — NO lockout incurred on shared personas. Stay ≤4 wrong then restore.
- BR-1 case-insensitive email confirmed (`TenantAdmin@ACME.test` → 200; email lowercased at AuthService.cs:61).
- ZERO cross-tenant writes; ISO arms read-only only. See [[qa-no-debugger-for-perf]], [[qa-personas-reseed-2026-06-25]].
