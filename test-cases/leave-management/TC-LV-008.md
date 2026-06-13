---
id: TC-LV-008
user_story: US-LV-001
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-008: Boundary -- max field values and name/code length limits

## 1. Test Objective
Verify that the system enforces field-length limits (name varchar(100), code varchar(20), color varchar(7)) and numeric precision limits (numeric(5,2) for entitlement/carry-forward), accepting values at the boundary and rejecting values that exceed it.

## 2. Related Requirements
- User Story: US-LV-001
- Acceptance Criteria: AC-1
- Functional Requirements: FR-2
- Data Requirements: Section 7

## 3. Preconditions
- Tenant "acme" exists.
- A user with `Leave.Configure` permission is authenticated.

## 4. Test Data
| Field | At Limit | Over Limit | Notes |
|-------|----------|------------|-------|
| Name | 100 chars ("A" x 100) | 101 chars ("A" x 101) | varchar(100) |
| Code | 20 chars ("X" x 20) | 21 chars ("X" x 21) | varchar(20) |
| Color | #FFFFFF (7 chars) | #FFFFFFA (8 chars) | varchar(7) |
| Annual Entitlement | 999.99 | 1000.00 | numeric(5,2) |
| Carry Forward Limit | 999.99 | 1000.00 | numeric(5,2) |
| Max Encash Days | 999.99 | 1000.00 | numeric(5,2) |
| Negative Balance Limit | 999.99 | 1000.00 | numeric(5,2) |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Create leave type with name of exactly 100 characters, code of 20 characters | Record created successfully. All fields stored correctly. |
| 2 | Attempt to create leave type with name of 101 characters | API returns 400 Bad Request. Validation error: "Name must not exceed 100 characters." |
| 3 | Attempt to create leave type with code of 21 characters | API returns 400. Validation error: "Code must not exceed 20 characters." |
| 4 | Create leave type with annual_entitlement = 999.99 | Record created successfully. |
| 5 | Attempt to create leave type with annual_entitlement = 1000.00 | API returns 400. Validation error: "Annual entitlement exceeds maximum value." |
| 6 | Create leave type with carry_forward_limit = 999.99 | Record created successfully. |
| 7 | Verify database stores numeric(5,2) values with correct precision | No rounding or truncation occurs for 2-decimal-place values. |

## 6. Postconditions
- Records at boundary limits are created and stored correctly.
- Records exceeding limits are not created.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
