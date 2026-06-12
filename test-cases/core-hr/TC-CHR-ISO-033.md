---
id: TC-CHR-ISO-033
user_story: US-CHR-009
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-ISO-033: Tenant A status change and employment history not visible to Tenant B

## 1. Test Objective
Verify that when an HR Officer in Tenant A changes an employee's status, the status change and resulting employment history entries are not visible to any user in Tenant B. This validates multi-tenant data isolation for status management (NFR-2).

## 2. Related Requirements
- User Story: US-CHR-009
- Non-Functional Requirements: NFR-2

## 3. Preconditions
- Tenant "acme" (Tenant A) exists with an HR Officer user and employee "John Smith" (`emp-a-uuid`) with status `active`.
- Tenant "globex" (Tenant B) exists with an HR Officer user.
- Both tenants are active and operational.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme | Subdomain: acme.yourhrm.com |
| Tenant B | globex | Subdomain: globex.yourhrm.com |
| Employee A | John Smith (emp-a-uuid) | In Tenant A, status: active |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Tenant A HR Officer, change John Smith's status from active to suspended with reason and effective date. | 200 OK. Employment history entry created for Tenant A. |
| 2 | As Tenant B HR Officer, query all employees: `GET /api/v1/tenant/employees`. | Response contains only Tenant B's employees. John Smith does NOT appear. |
| 3 | As Tenant B HR Officer, attempt to access John Smith's profile: `GET /api/v1/tenant/employees/emp-a-uuid`. | Response is 404 Not Found (the EF global query filter excludes cross-tenant data). |
| 4 | As Tenant B HR Officer, attempt to view John Smith's status change history (if endpoint exists). | 404 or empty result. No history entries from Tenant A are visible. |
| 5 | As Tenant B HR Officer, attempt to change John Smith's status: `POST /api/v1/tenant/employees/emp-a-uuid/status`. | Response is 404 Not Found (employee not found in Tenant B context). |
| 6 | Query the employment history table directly (DB level) with Tenant B's tenant_id. | No rows reference emp-a-uuid. The status change entry belongs exclusively to Tenant A's tenant_id. |

## 6. Postconditions
- Tenant A's status change and history are visible only within Tenant A.
- Tenant B has zero visibility into Tenant A's employee data or status changes.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
