---
id: TC-LV-023
user_story: US-LV-001
module: Leave Management
priority: high
type: performance
status: draft
created: 2026-06-13
---

# TC-LV-023: Leave type write API response within 800ms P95

## 1. Test Objective
Verify that leave type write operations (create, update, deactivate) complete within 800ms at the P95 percentile, consistent with the platform SLA for write operations.

## 2. Related Requirements
- User Story: US-LV-001
- Non-Functional Requirements: NFR-1

## 3. Preconditions
- Tenant "acme" exists.
- A user with `Leave.Configure` permission is authenticated.

## 4. Test Data
| Operation | Method | Endpoint | P95 Target |
|-----------|--------|----------|------------|
| Create | POST | /api/v1/leave-types | 800ms |
| Update | PUT | /api/v1/leave-types/{id} | 800ms |
| Deactivate | PATCH | /api/v1/leave-types/{id} | 800ms |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send 50 `POST /api/v1/leave-types` requests with unique valid data | All return 201 Created. Record response times. |
| 2 | Calculate P95 for create operations | P95 <= 800ms. |
| 3 | Send 50 `PUT /api/v1/leave-types/{id}` requests updating different fields | All return 200 OK. Record response times. |
| 4 | Calculate P95 for update operations | P95 <= 800ms. |
| 5 | Send 50 `PATCH /api/v1/leave-types/{id}` requests toggling is_active | All return 200 OK. Record response times. |
| 6 | Calculate P95 for deactivate operations | P95 <= 800ms. |

## 6. Postconditions
- All write operations meet the 800ms P95 SLA.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
