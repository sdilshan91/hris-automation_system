---
name: draft-iso-sweep-2026-07-03
description: 25 never-run draft tenant-ISO TCs executed post-BUG-003 — 22 PASS/0 FAIL/3 BLOCKED, zero leaks, no new findings
metadata:
  type: project
---

# Draft tenant-ISO sweep 2026-07-03 (REPORT-ONLY)

Executed 25 draft isolation TCs (17 Admin, 7 Core-HR, 1 Recruitment) against live :5000, API-layer.
**22 PASS / 0 FAIL / 3 BLOCKED. Zero cross-tenant leaks — no new findings.** Confirms [[bug003-fix-verified-2026-07-03]]
holds across another 25 TCs.

**Why:** post-BUG-003, `TenantAccessGuardMiddleware` rejects JWT-tenant != subdomain-tenant with 403 `cross_tenant_denied`.

**How to apply / reusable technique:**
- Probe pattern per TC: acme tenantadmin JWT (`tenantadmin@acme.test`/`Admin@123!`) vs two headers. Cross = `X-Tenant-Subdomain: techoneglobal` (id 019ef3c3-…) => **403 cross_tenant_denied**; same = `acme` => **200**. That's the whole isolation contract for `/api/v1/tenant/*` + `/api/v1/recruitment/*`.
- Guard body: `{"success":false,"code":"cross_tenant_denied",...}`. Distinguish from **authz-403** which has an EMPTY body (permission-policy gate). Same-arm empty-403 on `api/v1/system/*` = persona-gap (tenantadmin not SystemAdmin), NOT the guard.
- **System-context endpoints** (`api/v1/system/monitoring|plans|tenants/{id}/lifecycle`): tenantadmin => 403 both arms. Some ISO TCs (ADM-006/009/025) ASSERT that tenant denial => those PASS on the denial contract; others (ADM-005/008/026) need a SystemAdmin read/write baseline => BLOCKED persona-gap.
- CHR RLS/cache-key TCs (011/012/015/016/039/040): platform uses EF global query filters + guard, **Postgres RLS is deferred** — assert at API-guard layer, don't run direct-DB RLS (would falsely "fail" a not-implemented feature).
- No-context rejection (CHR-014): no header => 400 "Tenant context is not resolved"; nonexistent subdomain => 404 static "Workspace not found"; empty header (`-H "X-Tenant-Subdomain;"`) => 400.
- Routes: tenant users=`/api/v1/tenant/users`, settings=`/tenant/settings`, workflows=`/tenant/workflows`, audit=`/tenant/audit-logs`, employees=`/tenant/employees(/{id}/profile)`, recruit dashboard=`/api/v1/recruitment/dashboard`.
- Write-target ISO TCs (ADM-013/015/018/019/022): did READ-equivalent GET (cross-tenant WRITE safety-barred per [[test-runner-no-cross-tenant-write-probes]]).
