---
id: TC-ADM-ISO-027
user_story: US-ADM-009
module: Admin Console
priority: medium
type: security
status: blocked
created: 2026-06-17
---

# TC-ADM-ISO-027: [DEFERRED] PostgreSQL RLS DB-layer isolation for plan_limit_override

## 1. Test Objective
Cover the deferred defense-in-depth DB-layer isolation for the tenant-scoped `plan_limit_override` table: a PostgreSQL row-level-security policy keyed on `app.current_tenant_id` that blocks raw cross-tenant reads of overrides at the database layer.

DEFERRED — status: blocked. This platform enforces tenant isolation via the app (`ITenantContext`) + EF (global query filter / `TenantInterceptor`) layers only; PostgreSQL RLS is a deferred extension point (same family as US-ADM-001..008 / Payroll / Leave). `subscription_plan` itself is a SYSTEM-level table (not tenant-scoped), so its isolation is by system-vs-tenant context (TC-ADM-ISO-025); `plan_limit_override` carries `tenant_id` and is resolved per-tenant (TC-ADM-ISO-026) — RLS would add a DB-layer guard on top.

## 2. Related Requirements
- User Story: US-ADM-009
- Functional Requirements: FR-4 (plan_limit_override tenant scoping — DB-layer hardening)
- (Platform) RLS deferral family — see US-ADM-001 AC-6/FR-6

## 3. Preconditions
- (Deferred prerequisite) `plan_limit_override` has an RLS policy keyed on `app.current_tenant_id`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| RLS session var | app.current_tenant_id | not set today |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Confirm app+EF isolation today | Override resolution is tenant-scoped via ITenantContext + EF query filter (TC-ADM-ISO-026). |
| 2 | (When RLS implemented) Raw SQL on plan_limit_override without `app.current_tenant_id` set | Returns zero rows (RLS blocks). |
| 3 | (When RLS implemented) Raw SQL with another tenant's id set | Returns only that tenant's override rows. |
| 4 | Until implemented | Expected behavior: "Not available — PostgreSQL RLS is deferred platform infra; isolation today is app + EF query filter for plan_limit_override (TC-ADM-ISO-026) and system/tenant context for subscription_plan (TC-ADM-ISO-025)." |

## 6. Postconditions
- Deferred; no RLS asserted today.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
