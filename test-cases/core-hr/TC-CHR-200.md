---
id: TC-CHR-200
user_story: US-CHR-008
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-200: Unauthenticated request to document API returns 401

## 1. Test Objective
Verify that all document-related API endpoints require authentication. Requests without a valid JWT token receive a 401 Unauthorized response. No document data, metadata, or signed URLs are leaked.

## 2. Related Requirements
- User Story: US-CHR-008
- Functional Requirements: FR-6, FR-9, FR-10
- Non-Functional Requirements: NFR-2

## 3. Preconditions
- Tenant "acme" exists with employee "Jane Doe" (emp-001-uuid) who has a document "contract.pdf" (doc-001-uuid).
- No authentication token is provided in the requests.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Employee ID | emp-001-uuid | Has documents |
| Document ID | doc-001-uuid | Existing document |
| Auth Header | (none) | No JWT token |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `GET /api/v1/tenant/employees/{emp-001-uuid}/documents` without Authorization header. | Response status is 401 Unauthorized. No document list returned. |
| 2 | Send `POST /api/v1/tenant/employees/{emp-001-uuid}/documents` with a file but no Authorization header. | Response status is 401 Unauthorized. |
| 3 | Send `GET /api/v1/tenant/employees/{emp-001-uuid}/documents/{doc-001-uuid}/download` without Authorization header. | Response status is 401 Unauthorized. No signed URL returned. |
| 4 | Send `DELETE /api/v1/tenant/employees/{emp-001-uuid}/documents/{doc-001-uuid}` without Authorization header. | Response status is 401 Unauthorized. |
| 5 | Send requests with an expired JWT token. | All endpoints return 401 Unauthorized. |
| 6 | Send requests with a malformed JWT token. | All endpoints return 401 Unauthorized. |

## 6. Postconditions
- No document data was exposed.
- No state changes occurred in the database or object storage.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
