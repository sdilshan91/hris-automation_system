---
id: TC-ADM-ISO-001
user_story: US-ADM-001
module: Admin Console
priority: critical
type: security
status: fail
exec_note: >-
  2026-06-30 API: same-context reads correctly isolated (isoa sees only its 2 emp, isob only its 2). BUT isoa token + foreign X-Tenant-Subdomain:isob -> 200 + full isob rows LEAKED. Systemic cross-tenant leak = existing BUG-003 (token tenant_id never validated vs resolved subdomain). Admin/CoreHR surface LEAKS.
created: 2026-06-16
---

# TC-ADM-ISO-001: Newly provisioned tenant data is invisible to other tenants (cross-tenant READ block)

## 1. Test Objective
Verify AC-6: after provisioning Tenant A, none of A's seeded data (tenant settings, leave types, holiday template, workflow, owner membership) is visible to any other tenant, and A cannot see any other tenant's data. Reads are scoped by the EF Core global query filter (`TenantId == _tenantContext.TenantId`). A deliberate name-collision probe (identical leave-type names in A and B) confirms separation is by `tenant_id`, not by name.

## 2. Related Requirements
- User Story: US-ADM-001
- Acceptance Criteria: AC-6
- Functional Requirements: FR-6 (asserted via EF query filters; Postgres RLS deferred)
- Business Rules: BR-1
- Cross-cutting: mandatory multi-tenant isolation

## 3. Preconditions
- Tenant A (`alpha`) freshly provisioned; Tenant B (`beta`) pre-exists.
- Both have the same seeded default leave-type names (Annual, Sick, Casual) — a collision probe.
- Valid A-scoped and B-scoped JWTs available.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | alpha (freshly provisioned) | |
| Tenant B | beta | name-collision on seeded data |
| Probe entities | leave types, holiday template, tenant_setting, user_tenant | tenant-scoped tables |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As an alpha user, list leave types / settings / workflows | Returns ONLY alpha's rows; beta's identically named rows are absent (filtered by `TenantId`). |
| 2 | As a beta user, list the same resources | Returns ONLY beta's rows; alpha's freshly seeded rows are absent. |
| 3 | As alpha, list `user_tenant` memberships | Only alpha's owner membership is visible; beta memberships are not. |
| 4 | Cross-check counts | The union seen by A and by B equals the full seeded set, with zero overlap — separation is by `tenant_id`, not by name. |

## 6. Postconditions
- No cross-tenant read leakage of any provisioned/seeded data.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
