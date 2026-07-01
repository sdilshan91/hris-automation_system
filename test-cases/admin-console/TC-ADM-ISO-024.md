---
id: TC-ADM-ISO-024
user_story: US-ADM-008
module: Admin Console
priority: medium
type: security
status: blocked
exec_note: "S1: needs-CODE (feature-not-built/RLS) — dev backlog."
created: 2026-06-17
---

# TC-ADM-ISO-024: [DEFERRED] system_audit_log separation + PostgreSQL RLS DB-layer isolation

## 1. Test Objective
Cover two related DEFERRED isolation aspects of US-ADM-008:

(a) BR-3 `system_audit_log` separation — cross-tenant operations (impersonation, provisioning, lifecycle, retention-purge) belong to the SYSTEM audit log that is visible only to System Admins, NOT in this tenant audit console. The System Admin view is delivered by US-ADM-002/003 (System Admin persona); the tenant console here exposes only tenant-scoped `audit_log`. There is no separate `system_audit_log` surface added by this tenant story to assert against beyond the tenant/system context split.

(b) NFR-3 / PostgreSQL RLS DB-layer isolation — a defense-in-depth DB row-level-security layer for `audit_log`.

DEFERRED — status: blocked. The platform enforces tenant isolation via app (`ITenantContext`) + EF (global query filter / `TenantInterceptor`) layers only; PostgreSQL RLS is a deferred extension point (same family as US-ADM-001..007 / Payroll / Leave). System-vs-tenant audit separation is by context (system context vs resolved-tenant context), surfaced through the System Admin stories, not a tenant-console feature.

## 2. Related Requirements
- User Story: US-ADM-008
- Business Rules: BR-3 (system_audit_log visible only to System Admins; cross-tenant ops separate)
- Non-Functional Requirements: NFR-3 (DB-layer immutability/isolation — RLS deferred)

## 3. Preconditions
- (Deferred prerequisite for RLS) `audit_log` has an RLS policy keyed on `app.current_tenant_id`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| RLS session var | app.current_tenant_id | not set today |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Confirm tenant console scope | The tenant audit console exposes ONLY tenant-scoped `audit_log` rows (TC-ADM-ISO-021); cross-tenant/system operations are not surfaced here. |
| 2 | (When RLS implemented) Raw SQL on audit_log without `app.current_tenant_id` set | Returns zero rows (RLS blocks). |
| 3 | (When RLS implemented) Raw SQL with another tenant's id set | Returns only that tenant's rows. |
| 4 | Until implemented | Expected behavior: "Not available — system/tenant audit separation is by context via the System Admin stories (US-ADM-002/003); PostgreSQL RLS is deferred platform infra; isolation today is app + EF query filter (TC-ADM-ISO-021/-022/-023)." |

## 6. Postconditions
- Deferred; no RLS / separate system_audit_log surface asserted today.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
