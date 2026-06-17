---
id: TC-NTF-ISO-010
user_story: US-NTF-003
module: Notifications & Audit
priority: critical
type: security
status: draft
created: 2026-06-17
---

# TC-NTF-ISO-010: Cross-tenant preference ID injection -> 404; missing tenant context rejected

## 1. Test Objective
Verify that a user in Tenant A cannot read or modify preference records belonging to Tenant B by
injecting a Tenant B preference_id or a forged tenant_id, and that requests without a resolvable
tenant context are rejected. Cross-tenant access returns 404 (existence not disclosed), not 403.

## 2. Related Requirements
- User Story: US-NTF-003
- Acceptance Criteria: AC-5 (per-tenant-membership isolation)
- Functional Requirements: FR-8 (tenant_id + user_id set from session, never client-supplied)
- Non-Functional: NFR-2 (tenant isolation via EF Core global query filters; Postgres RLS deferred)
- Business Rules: BR-4 (per tenant membership)

## 3. Preconditions
- Tenant A (`adminA`/`emp1`) and Tenant B (`emp2`) are active; Tenant B has preference records.
- `emp1` is authenticated within Tenant A.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant B preference_id | <emp2's record id> | for cross-tenant IDOR |
| forged tenant_id in body | Tenant B | injection attempt |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As `emp1` (Tenant A), GET a Tenant B preference by its preference_id | 404 Not Found in Tenant A scope (existence not disclosed) -- NOT 403; EF global query filter excludes the row |
| 2 | As `emp1`, PUT a change to a Tenant B preference_id | 404 Not Found; no write performed; Tenant B record unchanged |
| 3 | As `emp1`, PUT a preference with a forged body tenant_id = Tenant B | Server IGNORES the client tenant_id and uses the session-resolved Tenant A; the change (if any) is stamped tenant_id=Tenant A only (TenantInterceptor) |
| 4 | Call a preference endpoint with no resolvable tenant context | Request rejected before any preference query; no cross-tenant data returned |
| 5 | Inspect persisted rows | No Tenant B row was read or modified by a Tenant A actor; every written row carries the correct session tenant_id |

## 6. Postconditions
- Cross-tenant preference access is impossible via ID/tenant injection; isolation intact.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
