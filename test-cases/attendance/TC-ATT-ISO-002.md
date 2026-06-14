---
id: TC-ATT-ISO-002
user_story: US-ATT-001
module: Attendance
priority: critical
type: security
status: draft
created: 2026-06-14
---

# TC-ATT-ISO-002: Clock-in API rejects requests without a valid resolved tenant context

## 1. Test Objective
Verify that the clock-in endpoint refuses to create an `attendance_log` when no valid tenant context can be resolved — e.g., an unknown/inactive subdomain, a missing `X-Tenant-Subdomain` dev header with no resolvable host, or a JWT whose `tenant_id` does not correspond to an active tenant. No record may be created without an authoritative tenant scope.

## 2. Related Requirements
- User Story: US-ATT-001
- Non-Functional Requirements: NFR-2
- Functional Requirements: FR-1 (tenant_id from session context)

## 3. Preconditions
- Tenant "acme" exists and is `active`.
- Subdomain "ghost" does NOT correspond to any active tenant.
- Employee "Jordan Lee" is a valid acme user (used for the positive control).

## 4. Test Data
| Sub-case | Tenant signal | Expected |
|----------|---------------|----------|
| A | No `X-Tenant-Subdomain` / unresolvable host | Rejected (400/401), no record |
| B | `X-Tenant-Subdomain: ghost` (unknown tenant) | Rejected (404/400), no record |
| C | Subdomain of an inactive/suspended tenant | Rejected, no record |
| D | JWT `tenant_id` mismatched with the resolved subdomain | Rejected (401/403), no record |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Sub-case A: `POST /api/v1/attendance/clock-in` with no resolvable tenant | Request rejected; no `attendance_log` created. |
| 2 | Sub-case B: send with `X-Tenant-Subdomain: ghost` | Tenant resolution fails; request rejected; no record. |
| 3 | Sub-case C: send for a suspended tenant | Request rejected; no record. |
| 4 | Sub-case D: valid acme JWT but `X-Tenant-Subdomain: globex` | Tenant/claim mismatch rejected; no cross-tenant write occurs. |
| 5 | Positive control: valid acme subdomain + matching acme JWT | 201 Created with `tenant_id` = acme. |

## 6. Postconditions
- No `attendance_log` records created from any request lacking a valid, matching tenant context.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
