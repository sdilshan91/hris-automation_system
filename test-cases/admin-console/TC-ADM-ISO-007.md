---
id: TC-ADM-ISO-007
user_story: US-ADM-003
module: Admin Console
priority: critical
type: security
status: pass
created: 2026-06-16
---

# TC-ADM-ISO-007: Tenant isolation during impersonation — Tenant A session cannot reach Tenant B data (404)

## 1. Test Objective
Verify FR-6 and the Test-Hint tenant-isolation requirement: an impersonation token scoped to Tenant A carries Tenant A's `tenant_id`. Any attempt to read or mutate Tenant B's data from within that session is blocked by the EF Core global query filters / tenant resolution and returns 404 (existence not disclosed — consistent with the module's 404-not-403 convention for cross-tenant ID injection).

## 2. Related Requirements
- User Story: US-ADM-003
- Functional Requirements: FR-6
- Business Rules: BR-1
- Test Hints: Tenant isolation (Tenant A impersonation -> Tenant B data -> 404)

## 3. Preconditions
- Two active tenants: Acme (A) and Beta (B), each with its own employees/records and known IDs.
- An Active impersonation session for a user in Acme (token `tenant_id`=Acme).
- NOTE (platform accuracy): isolation is enforced today by EF Core global query filters (read) + `TenantInterceptor` (write stamping), NOT PostgreSQL RLS — RLS is a deferred platform extension point (same family as the US-ADM-001 RLS note). Tests assert the EF mechanism in force.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| impersonation tenant | Acme (A) | token tenant_id |
| cross-tenant target | a known Beta (B) record id | belongs to another tenant |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | With the Acme impersonation token, GET a Beta record by its real id (IDOR-style) | 404 — the EF query filter scopes to Acme; Beta's row is invisible (existence not disclosed). |
| 2 | With the Acme imp token, attempt to update/delete a Beta record by id | 404 (or 403 if destructive-op block fires first per FR-6) — no Beta data is mutated. |
| 3 | List a tenant-scoped collection under the Acme imp token | Only Acme rows are returned; zero Beta rows leak into the result. |
| 4 | Confirm Beta data unchanged after the cross-tenant attempts | Beta records are intact; no audit row indicates a successful cross-tenant access. |

## 6. Postconditions
- An impersonation session is confined to its tenant; cross-tenant access yields 404 with no data leakage or mutation.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
