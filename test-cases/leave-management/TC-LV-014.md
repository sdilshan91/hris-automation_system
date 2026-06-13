---
id: TC-LV-014
user_story: US-LV-001
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-13
---

# TC-LV-014: Unauthenticated request to leave types API returns 401

## 1. Test Objective
Verify that all leave type API endpoints require authentication and return 401 Unauthorized when called without a valid JWT token.

## 2. Related Requirements
- User Story: US-LV-001
- Preconditions: Section 2
- Dependencies: US-AUTH-*

## 3. Preconditions
- Leave types API endpoints are deployed and accessible.
- No authentication token is included in the request.

## 4. Test Data
| Endpoint | Method | Auth | Notes |
|----------|--------|------|-------|
| /api/v1/leave-types | GET | None | List |
| /api/v1/leave-types | POST | None | Create |
| /api/v1/leave-types/{id} | GET | None | Get by ID |
| /api/v1/leave-types/{id} | PUT | None | Update |
| /api/v1/leave-types/{id} | PATCH | None | Partial update |
| /api/v1/leave-types/{id} | DELETE | None | Delete |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `GET /api/v1/leave-types` without Authorization header | 401 Unauthorized. Response body contains error message. No data returned. |
| 2 | Send `POST /api/v1/leave-types` without Authorization header | 401 Unauthorized. |
| 3 | Send `GET /api/v1/leave-types/{valid_id}` without Authorization header | 401 Unauthorized. |
| 4 | Send `PUT /api/v1/leave-types/{valid_id}` without Authorization header | 401 Unauthorized. |
| 5 | Send `PATCH /api/v1/leave-types/{valid_id}` without Authorization header | 401 Unauthorized. |
| 6 | Send `GET /api/v1/leave-types` with an expired JWT token | 401 Unauthorized. |
| 7 | Send `GET /api/v1/leave-types` with a malformed JWT token ("Bearer invalid.token.here") | 401 Unauthorized. |

## 6. Postconditions
- No data was returned or modified.
- All unauthenticated requests were correctly rejected.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
