---
id: TC-CHR-ISO-041
user_story: US-CHR-011
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-ISO-041: Tenant A cannot see Tenant B's direct reports or reporting structure

## 1. Test Objective
Verify that manager-to-direct-report relationships are strictly tenant-isolated. A manager in Tenant A querying direct reports cannot see employees from Tenant B, and vice versa. The direct-reports API endpoint enforces tenant scoping via EF Core global query filters and RLS. This validates NFR-3.

## 2. Related Requirements
- User Story: US-CHR-011
- Non-Functional Requirements: NFR-3
- Functional Requirements: FR-9

## 3. Preconditions
- Tenant "acme" and Tenant "globex" both exist with status `active`.
- HR Officer users exist in both tenants.
- In Tenant "acme": Manager A has 3 direct reports (E1, E2, E3).
- In Tenant "globex": Manager G has 2 direct reports (G1, G2).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A Subdomain | acme.yourhrm.com | Manager A with 3 reports |
| Tenant B Subdomain | globex.yourhrm.com | Manager G with 2 reports |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate as HR Officer in Tenant A. Query `GET /api/v1/tenant/employees/{A.id}/direct-reports`. | Returns 3 employees (E1, E2, E3). All have tenant_id = acme. |
| 2 | From Tenant A context, attempt to query Manager G's direct reports: `GET /api/v1/tenant/employees/{G.id}/direct-reports`. | 404 Not Found (Manager G's UUID does not exist within Tenant A's scope). |
| 3 | Authenticate as HR Officer in Tenant B. Query `GET /api/v1/tenant/employees/{G.id}/direct-reports`. | Returns 2 employees (G1, G2). All have tenant_id = globex. |
| 4 | From Tenant B context, attempt to query Manager A's direct reports: `GET /api/v1/tenant/employees/{A.id}/direct-reports`. | 404 Not Found. |
| 5 | From Tenant B, search for E1's name in the employee directory. | Zero results. Tenant A's employees are invisible. |

## 6. Postconditions
- No cross-tenant data leakage. Each tenant sees only its own reporting structure.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
