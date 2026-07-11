---
id: TC-ADM-ISO-023
user_story: US-ADM-008
module: Admin Console
priority: critical
type: security
status: pass
exec_note: "2026-07-03 API-layer isolation probe (acme tenantadmin JWT): cross-tenant arm (X-Tenant-Subdomain: techoneglobal) => 403 cross_tenant_denied; same-tenant arm (acme) => 200. TenantAccessGuardMiddleware enforced. No leak."
created: 2026-06-17
---

# TC-ADM-ISO-023: Audit endpoints require a resolved tenant context + a read role

## 1. Test Objective
Verify AC-1 / BR-1 / BR-3: the audit list, detail, and export endpoints require BOTH a resolved tenant context (`ITenantContext.TenantId`) AND one of the permitted read roles. A request with no/invalid tenant context is rejected; a request with a valid tenant context but a non-permitted role is 403; the export-audit row written by an export is itself tenant-stamped to the acting tenant.

## 2. Related Requirements
- User Story: US-ADM-008
- Acceptance Criteria: AC-1 (tenant-scoped + access gated), AC-4 (export-audit stamped)
- Business Rules: BR-1 (read roles), BR-3 (tenant scope)

## 3. Preconditions
- Tenant Alpha active; Dana `TenantAdmin` of Alpha, Alex `Auditor` of Alpha, Eve `Employee` of Alpha.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| valid context | acme subdomain + Dana token | |
| missing context | reserved/admin subdomain or no tenant header | |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | GET list with no resolved tenant context | Rejected (no tenant scope to query). |
| 2 | GET list with valid Alpha context + Dana (TenantAdmin) | 200, Alpha rows. |
| 3 | GET list with valid Alpha context + Eve (Employee) | 403 (role not permitted, BR-1). |
| 4 | As Dana, export | The "AuditLog.Export" row is written with TenantId = Alpha (TenantInterceptor stamp), visible only to Alpha. |
| 5 | Confirm export-audit isolation | Beta never sees Alpha's "AuditLog.Export" row. |

## 6. Postconditions
- One Alpha-stamped export-audit row from step 4; no cross-tenant leakage.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
