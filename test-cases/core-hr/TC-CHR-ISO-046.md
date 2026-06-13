---
id: TC-CHR-ISO-046
user_story: US-CHR-012
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-13
---

# TC-CHR-ISO-046: API rejects custom field requests without valid tenant context

## 1. Test Objective
Verify that all custom field API endpoints reject requests that lack a valid tenant context (missing or invalid subdomain/tenant header). The system must not fall back to a default tenant or return data from any tenant. This validates NFR-2.

## 2. Related Requirements
- User Story: US-CHR-012
- Non-Functional Requirements: NFR-2

## 3. Preconditions
- Custom field definitions exist in at least one tenant.
- API server is running.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Valid Subdomain | acme.yourhrm.com | Has custom fields |
| Invalid Subdomain | nonexistent.yourhrm.com | No such tenant |
| Missing Subdomain | (none) | No tenant header/subdomain |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `GET /api/v1/tenant/custom-fields?entityType=Employee` with valid JWT but no `X-Tenant-Subdomain` header and no subdomain in host. | HTTP 400 or 401 -- request rejected due to missing tenant context. No data returned. |
| 2 | Send the same request with `X-Tenant-Subdomain: nonexistent`. | HTTP 404 or 400 -- tenant not found. No data returned. |
| 3 | Send `POST /api/v1/tenant/custom-fields` with valid JWT but missing tenant context. | HTTP 400 or 401 -- rejected. No field created. |
| 4 | Send `PUT /api/v1/tenant/custom-fields/{id}` with valid JWT but wrong tenant context (field belongs to acme, request comes from globex). | HTTP 404 -- the field UUID does not exist in globex's scope. No modification. |

## 6. Postconditions
- No data is returned or modified without a valid tenant context.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
