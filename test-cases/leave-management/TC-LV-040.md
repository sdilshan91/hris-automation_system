---
id: TC-LV-040
user_story: US-LV-002
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-13
---

# TC-LV-040: Unauthenticated request to entitlement API returns 401

## 1. Test Objective
Verify that all entitlement rule and override API endpoints reject unauthenticated requests with 401 Unauthorized. No entitlement data is exposed without valid authentication.

## 2. Related Requirements
- User Story: US-LV-002
- Preconditions: Section 2

## 3. Preconditions
- Tenant "acme" exists with leave entitlement rules configured.
- No authentication token is provided in the requests.

## 4. Test Data
| Endpoint | Method | Auth | Expected |
|----------|--------|------|----------|
| /api/v1/leave-entitlement-rules | GET | None | 401 |
| /api/v1/leave-entitlement-rules | POST | None | 401 |
| /api/v1/leave-entitlement-rules/{id} | PUT | None | 401 |
| /api/v1/leave-entitlement-rules/{id} | DELETE | None | 401 |
| /api/v1/leave-entitlement-overrides | POST | None | 401 |
| /api/v1/leave-entitlement-overrides/{id} | DELETE | None | 401 |
| /api/v1/leave-entitlement-rules | GET | Expired JWT | 401 |
| /api/v1/leave-entitlement-rules | GET | Malformed JWT | 401 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `GET /api/v1/leave-entitlement-rules` without Authorization header | 401 Unauthorized. No data in response body. |
| 2 | Send `POST /api/v1/leave-entitlement-rules` without Authorization header | 401 Unauthorized. |
| 3 | Send `PUT /api/v1/leave-entitlement-rules/{existing_id}` without Authorization header | 401 Unauthorized. |
| 4 | Send `DELETE /api/v1/leave-entitlement-rules/{existing_id}` without Authorization header | 401 Unauthorized. |
| 5 | Send `POST /api/v1/leave-entitlement-overrides` without Authorization header | 401 Unauthorized. |
| 6 | Send `DELETE /api/v1/leave-entitlement-overrides/{existing_id}` without Authorization header | 401 Unauthorized. |
| 7 | Send `GET /api/v1/leave-entitlement-rules` with an expired JWT token | 401 Unauthorized. |
| 8 | Send `GET /api/v1/leave-entitlement-rules` with a malformed JWT token (e.g., "Bearer invalid_token") | 401 Unauthorized. |
| 9 | Verify 401 responses do not contain stack traces, entity data, or internal error details | Response body is a generic error message or empty. |

## 6. Postconditions
- No entitlement data was exposed to unauthenticated clients.
- All 401 responses are consistent in format.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
