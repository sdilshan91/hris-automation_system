---
id: TC-ADM-ISO-009
user_story: US-ADM-004
module: Admin Console
priority: critical
type: security
status: draft
created: 2026-06-17
---

# TC-ADM-ISO-009: Lifecycle endpoints require system context; cross-tenant tenant-id injection returns 404 (not 403)

## 1. Test Objective
Verify the lifecycle transition endpoints are confined to the system/admin context and resist cross-tenant ID injection (IDOR). A tenant-scoped caller cannot drive transitions; and probing a tenant_id from outside the proper system context does not disclose existence — per the module convention, cross-tenant ID injection returns 404 (existence not disclosed), not 403.

## 2. Related Requirements
- User Story: US-ADM-004
- Functional Requirements: FR-1, FR-2, FR-5, FR-6, FR-7
- Business Rules: BR-7
- Test Hints: (tenant isolation / access control)

## 3. Preconditions
- System Admin at `admin.yourhrm.com`; a tenant-scoped user at `acme.yourhrm.com`.
- Two non-system tenants Acme and Beta with known IDs.
- NOTE (platform accuracy): cross-tenant resolution/visibility is enforced by EF Core global query filters + tenant resolution; the 404-not-403 convention for ID injection is the module standard (RLS deferred).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| acme id | acme-tenant-uuid | caller's own tenant |
| beta id | beta-tenant-uuid | a different tenant |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | From the Acme tenant context (tenant user), call a lifecycle endpoint with no system context | Rejected — tenant users cannot reach the admin lifecycle API (403/404; cross-ref TC-ADM-004-15). |
| 2 | From the Acme tenant context, inject Beta's tenant_id into a lifecycle request | 404 — existence not disclosed (404-not-403 convention); Beta is untouched. |
| 3 | From a non-system/improper context, request lifecycle history for an arbitrary tenant_id | 404 — no cross-tenant disclosure. |
| 4 | Confirm no transition occurred and no audit row for a successful cross-tenant action | Target tenants unchanged; no spurious lifecycle/audit rows. |

## 6. Postconditions
- Lifecycle transitions and history require system context; cross-tenant ID injection yields 404 with no data leakage or state change.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
