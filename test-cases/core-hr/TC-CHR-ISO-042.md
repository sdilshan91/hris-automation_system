---
id: TC-CHR-ISO-042
user_story: US-CHR-011
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-ISO-042: API rejects manager assignment requests without valid tenant context

## 1. Test Objective
Verify that the manager assignment API and direct-reports query API reject requests that lack a valid tenant context (missing or invalid subdomain/tenant header). This validates NFR-3 and FR-9.

## 2. Related Requirements
- User Story: US-CHR-011
- Non-Functional Requirements: NFR-3
- Functional Requirements: FR-9

## 3. Preconditions
- Employee E and Manager M exist in Tenant "acme".
- The test client has a valid JWT but sends requests without a tenant subdomain or with an invalid subdomain.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Valid subdomain | acme.yourhrm.com | Correct tenant |
| Missing subdomain | (none) | No X-Tenant-Subdomain header |
| Invalid subdomain | nonexistent.yourhrm.com | No matching tenant |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `PUT /api/v1/tenant/employees/{E.id}` to assign Manager M without any tenant subdomain header. | HTTP 400 or 401/403 indicating missing tenant context. The request does not execute. |
| 2 | Send `GET /api/v1/tenant/employees/{M.id}/direct-reports` without tenant subdomain header. | Same rejection as step 1. |
| 3 | Send the assignment request with `X-Tenant-Subdomain: nonexistent`. | HTTP 400 or 404 indicating tenant not found. |
| 4 | Send the direct-reports query with `X-Tenant-Subdomain: nonexistent`. | Same rejection as step 3. |
| 5 | Verify no state changes occurred on Employee E's record. | `reports_to_employee_id` is unchanged. |

## 6. Postconditions
- No state change. All requests without valid tenant context were rejected.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
