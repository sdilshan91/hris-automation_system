---
id: TC-CHR-254
user_story: US-CHR-010
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-254: Unauthenticated request to bulk import API returns 401

## 1. Test Objective
Verify that an unauthenticated request (no JWT, expired JWT, or invalid JWT) to the bulk import API endpoint returns 401 Unauthorized. No processing or data access should occur.

## 2. Related Requirements
- User Story: US-CHR-010
- Preconditions (Section 2): User must be authenticated

## 3. Preconditions
- The bulk import API endpoint exists and is deployed.
- A valid import file is available for the request body.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Endpoint | POST /api/v1/tenant/employees/import | Bulk import endpoint |
| Authorization | (none / expired / invalid) | Unauthenticated scenarios |
| File | valid_import.csv | Valid file content |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send POST to the import endpoint with no Authorization header, attaching `valid_import.csv`. | Response: 401 Unauthorized. Body contains no employee data or import results. |
| 2 | Send POST to the import endpoint with an expired JWT token. | Response: 401 Unauthorized. |
| 3 | Send POST to the import endpoint with a malformed JWT token (`Bearer invalid-token`). | Response: 401 Unauthorized. |
| 4 | Verify no employees were created in any tenant. | Employee count unchanged across all tenants. |

## 6. Postconditions
- No data access or modification occurred. System state unchanged.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
