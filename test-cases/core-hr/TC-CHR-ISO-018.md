---
id: TC-CHR-ISO-018
user_story: US-CHR-003
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-ISO-018: API rejects directory requests without valid tenant context

## 1. Test Objective
Verify that the Employee Directory API endpoints reject requests that lack a valid tenant context (no subdomain resolution, missing or invalid X-Tenant-Subdomain header). The API must not return employee data without proper tenant identification.

## 2. Related Requirements
- User Story: US-CHR-003
- Functional Requirements: FR-7
- Non-Functional Requirements: NFR-3

## 3. Preconditions
- Multiple tenants exist with employees.
- User has a valid JWT but the tenant context is not resolved.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Missing subdomain | No X-Tenant-Subdomain header and no subdomain in Host | |
| Invalid subdomain | nonexistent-tenant-xyz | Tenant does not exist |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `GET /api/v1/tenant/employees/directory` with valid JWT but no tenant context (no subdomain header) | Response is 400 or 404 -- tenant resolution fails. No employee data returned. |
| 2 | Send `GET /api/v1/tenant/employees/directory` with X-Tenant-Subdomain: "nonexistent-tenant-xyz" | Response is 404 -- tenant not found. No employee data returned. |
| 3 | Send `GET /api/v1/tenant/employees/directory/export?format=Csv` with no tenant context | Response is 400 or 404. No file download occurs. |
| 4 | Verify response bodies contain no employee data | Error responses contain only error messages, no PII or employee records. |

## 6. Postconditions
- No data was exposed.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
