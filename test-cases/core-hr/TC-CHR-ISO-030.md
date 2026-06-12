---
id: TC-CHR-ISO-030
user_story: US-CHR-008
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-ISO-030: API rejects document requests without valid tenant context

## 1. Test Objective
Verify that all document-related API endpoints reject requests that lack a valid tenant context (no subdomain resolved, no tenant in JWT, or invalid tenant). The `TenantResolutionMiddleware` must block these requests before they reach document logic. This validates NFR-2.

## 2. Related Requirements
- User Story: US-CHR-008
- Non-Functional Requirements: NFR-2

## 3. Preconditions
- Tenant "acme" exists with employee "Jane Doe" (emp-001-uuid) who has documents.
- A valid JWT is available but the request is sent without a tenant-resolvable subdomain or `X-Tenant-Subdomain` header.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Employee ID | emp-001-uuid | Has documents |
| Host Header | api.yourhrm.com | No tenant subdomain |
| X-Tenant-Subdomain | (absent) | No tenant context |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `GET /api/v1/tenant/employees/{emp-001-uuid}/documents` with Host header `api.yourhrm.com` (no tenant subdomain) and no `X-Tenant-Subdomain` header. | Response returns 400 Bad Request or 403 Forbidden with message indicating tenant context is required. |
| 2 | Send `POST /api/v1/tenant/employees/{emp-001-uuid}/documents` (upload) without tenant context. | Response returns 400 or 403. No file is stored. |
| 3 | Send `GET /api/v1/tenant/employees/{emp-001-uuid}/documents/{doc-id}/download` without tenant context. | Response returns 400 or 403. No signed URL is generated. |
| 4 | Send `DELETE /api/v1/tenant/employees/{emp-001-uuid}/documents/{doc-id}` without tenant context. | Response returns 400 or 403. No deletion occurs. |
| 5 | Send requests with an invalid tenant subdomain (e.g., `nonexistent.yourhrm.com`). | Response returns 404 (tenant not found) or 400. |
| 6 | Send requests with a deactivated tenant subdomain. | Response returns 403 (tenant is inactive). |

## 6. Postconditions
- No document data was accessed or modified.
- TenantResolutionMiddleware correctly blocked all requests without valid tenant context.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
