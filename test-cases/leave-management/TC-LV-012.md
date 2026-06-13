---
id: TC-LV-012
user_story: US-LV-001
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-13
---

# TC-LV-012: Same leave type name allowed in different tenants (cross-tenant uniqueness)

## 1. Test Objective
Verify that two different tenants can each create a leave type with the same name (e.g., "Annual Leave"), confirming that uniqueness is scoped to the tenant level, not globally.

## 2. Related Requirements
- User Story: US-LV-001
- Acceptance Criteria: AC-1
- Functional Requirements: FR-1
- Non-Functional Requirements: NFR-2
- Business Rules: BR-1

## 3. Preconditions
- Tenant "acme" exists with no leave type named "Annual Leave".
- Tenant "globex" exists with no leave type named "Annual Leave".
- HR Officers authenticated in each respective tenant.

## 4. Test Data
| Field | Tenant A (acme) | Tenant B (globex) |
|-------|----------------|-------------------|
| Name | Annual Leave | Annual Leave |
| Code | AL | AL |
| Entitlement | 20 days | 25 days |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As acme HR Officer, create "Annual Leave" with 20 days entitlement | 201 Created. Leave type saved with `tenant_id = acme_id`. |
| 2 | As globex HR Officer, create "Annual Leave" with 25 days entitlement | 201 Created. Leave type saved with `tenant_id = globex_id`. No conflict. |
| 3 | Verify both records exist in the database | `SELECT * FROM leave_type WHERE name = 'Annual Leave'` returns 2 rows with different `tenant_id` values. |
| 4 | As acme user, `GET /api/v1/leave-types` | Returns only acme's "Annual Leave" with 20 days. Globex's type not visible. |
| 5 | As globex user, `GET /api/v1/leave-types` | Returns only globex's "Annual Leave" with 25 days. Acme's type not visible. |

## 6. Postconditions
- Both tenants have their own "Annual Leave" leave type, isolated from each other.
- No data leakage between tenants.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
