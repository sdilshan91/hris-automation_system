---
id: TC-ADM-ISO-026
user_story: US-ADM-009
module: Admin Console
priority: critical
type: security
status: draft
created: 2026-06-17
---

# TC-ADM-ISO-026: PlanLimitOverride is tenant-scoped — an override for Tenant X applies only to Tenant X

## 1. Test Objective
Verify FR-4 + AC-5 isolation: a `plan_limit_override` is linked to a specific `tenant_id` and resolves ONLY for that tenant. An override created for Tenant X (`max_employees=200`) does not raise the limit for any other tenant on the same plan — those tenants continue resolving the plan field. Override resolution never leaks across tenants.

## 2. Related Requirements
- User Story: US-ADM-009
- Acceptance Criteria: AC-5 (per-tenant overrides)
- Functional Requirements: FR-4 (override keyed by tenant_id; resolution order)

## 3. Preconditions
- Plan `starter` has `max_employees=50`. Tenants "Acme" and "Beta" are BOTH on `starter`.
- An override {tenant=Acme, limit_key=max_employees, value=200, expires_at=null} exists.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| plan starter max_employees | 50 | base for both |
| Acme override | max_employees=200 | applies to Acme only |
| Beta | no override | resolves plan field |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Resolve max_employees for Acme | 200 — Acme's non-expired override wins. |
| 2 | Resolve max_employees for Beta (same plan, no override) | 50 — plan field; Acme's override does NOT leak to Beta. |
| 3 | As Acme tenant admin, create up to 200 employees | Allowed up to 200 (override). |
| 4 | As Beta tenant admin, attempt the 51st employee | Rejected at 50 — Beta unaffected by Acme's override. |
| 5 | Delete the Acme override; resolve Acme | Falls back to 50 (plan field) — Beta still 50; no cross-tenant residue. |

## 6. Postconditions
- Override effect strictly scoped to its tenant_id.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
