---
id: TC-ADM-ISO-010
user_story: US-ADM-005
module: Admin Console
priority: critical
type: security
status: draft
created: 2026-06-17
---

# TC-ADM-ISO-010: User list is tenant-scoped — Tenant A admin never sees Tenant B users (EF query-filter READ block)

## 1. Test Objective
Verify AC-1 / AC-6 / BR-1 cross-tenant READ isolation for the user-management list: a Tenant Admin authenticated on Tenant A sees ONLY Tenant A's `user_tenant` memberships. EF Core global query filters scope the join (`user_tenant` x `users` x `user_tenant_role`) to `ITenantContext.TenantId`, so no Tenant B user, email, role, or invitation is ever returned — across list, search, filter, and detail endpoints.

## 2. Related Requirements
- User Story: US-ADM-005
- Acceptance Criteria: AC-1, AC-6
- Functional Requirements: FR-1
- Business Rules: BR-1
- NOTE (platform accuracy): isolation enforced by EF Core global query filters (read) + `TenantInterceptor` (write stamping); Postgres RLS is a deferred extension point (TC-ADM-005-21).

## 3. Preconditions
- Tenant Admin "Dana" authenticated on Acme (Tenant A).
- Beta (Tenant B) has distinct users/emails/invitations, including an email that also exists in Acme (a shared global user) and an email unique to Beta.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| caller | Dana @ Acme | Tenant A |
| Beta-only email | only-beta@beta.io | must never appear |
| shared global user | sam@globex.com | Acme view shows ONLY Acme membership row |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Dana, `GET /api/v1/admin/users` (all pages) | Only Acme memberships; total count matches Acme only. |
| 2 | Search for Beta's unique email `only-beta@beta.io` | Zero results — Beta data not reachable. |
| 3 | Filter by a role that exists in Beta but holds no Acme members | Empty result for Acme; no Beta rows surface. |
| 4 | View detail for the shared global user (sam) | Shows ONLY the Acme membership/roles/invitations; Beta membership of the same global user is NOT shown. |
| 5 | Inspect pagination total | Count reflects Acme memberships only — Beta rows never inflate the total. |

## 6. Postconditions
- No data mutated. Tenant A's roster view is provably confined to Tenant A.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
