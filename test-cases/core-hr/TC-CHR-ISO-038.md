---
id: TC-CHR-ISO-038
user_story: US-CHR-010
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-ISO-038: API rejects bulk import requests without valid tenant context

## 1. Test Objective
Verify that the bulk import API endpoint rejects requests that lack a valid tenant context (e.g., no subdomain resolved, missing X-Tenant-Subdomain header in dev, or an invalid/unknown subdomain). The system must not process any rows without knowing which tenant to assign.

## 2. Related Requirements
- User Story: US-CHR-010
- Non-Functional Requirements: NFR-2
- Business Rules: BR-1

## 3. Preconditions
- The bulk import API endpoint is deployed and accessible.
- A valid import file is available.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Endpoint | POST /api/v1/tenant/employees/import | Bulk import endpoint |
| Subdomain Scenarios | (none), unknown.yourhrm.com, admin.yourhrm.com | Invalid/missing tenant contexts |
| File | valid_import.csv | 3 valid rows |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send POST to the import endpoint without any tenant subdomain header or subdomain-based routing. | Response: 400 or 401 indicating tenant context is required. No employees created. |
| 2 | Send POST with subdomain `unknown.yourhrm.com` (a tenant that does not exist). | Response: 400 or 404 indicating the tenant is not found. No employees created. |
| 3 | Send POST with subdomain `admin.yourhrm.com` (reserved system subdomain). | Response: 400 or 403 indicating this is a reserved subdomain. No employees created. |
| 4 | Verify the database. | No employees were created in any tenant. |

## 6. Postconditions
- No data modification occurred. Tenant resolution is a prerequisite for import.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
