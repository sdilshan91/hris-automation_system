---
id: TC-LV-ISO-012
user_story: US-LV-003
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-13
---

# TC-LV-ISO-012: Balance cache keys and attachment storage paths are tenant-scoped

## 1. Test Objective
Verify that the leave-balance cache keys consulted during application (NFR-2) and the attachment blob storage paths (NFR-3) are tenant-scoped, so that no cross-tenant balance value or attachment is ever served when applying for leave.

> **Note:** Redis balance caching is currently DEFERRED across the leave module (see vault `modules/leave-management.md`). Where the cache layer is not yet implemented, the cache-key portion of this test is verified against the DB-fallback balance path and the documented key pattern; the attachment-path portion (NFR-3) is fully testable now.

## 2. Related Requirements
- User Story: US-LV-003
- Non-Functional Requirements: NFR-2, NFR-3

## 3. Preconditions
- Tenant "acme" has employee "Jane Smith" with an Annual Leave balance of 10 days.
- Tenant "globex" has an employee with the same employee_no/UUID-shaped id and a different balance (e.g., 3 days).
- Both tenants have attachments stored for prior leave requests.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Balance cache key pattern | tenant:{tenantId}:leave_balance:{employeeId}:{leaveTypeId} | Tenant-scoped (FR-2/NFR-2) |
| acme attachment path | acme-tenant-id/leaves/{requestId}/ | Tenant-scoped (NFR-3) |
| globex attachment path | globex-tenant-id/leaves/{requestId}/ | Tenant-scoped (NFR-3) |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | In acme context, read Jane's Annual Leave balance on the apply form | Returns acme's value (10 days). The cache key (or DB-fallback query) is scoped by acme's tenant id. |
| 2 | In globex context, read the same-shaped employee/leave-type balance | Returns globex's value (3 days) -- never acme's, even if employee/leave-type ids collide. |
| 3 | Inspect the cache key used (or the documented key pattern if caching deferred) | Key includes the tenant id segment; no shared/global key across tenants. |
| 4 | Submit a leave request with an attachment in acme | File stored under `acme-tenant-id/leaves/{requestId}/`. |
| 5 | Attempt to access the acme attachment URL/path while authenticated in globex | Access denied (403/404); attachment paths are not cross-tenant accessible. |
| 6 | Invalidate/refresh acme's balance and confirm globex's cached/served balance is unaffected | Tenant balance state is independent; no cross-tenant invalidation or leakage. |

## 6. Postconditions
- Balance values and attachment paths are strictly tenant-scoped.
- No cross-tenant balance or attachment is served during leave application.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
