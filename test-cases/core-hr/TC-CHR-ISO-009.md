---
id: TC-CHR-ISO-009
user_story: US-CHR-001
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-ISO-009: Tenant A cannot see Tenant B's employees

## 1. Test Objective
Verify that employee records are fully isolated between tenants. A user authenticated in Tenant A must not be able to see, list, or access any employee records belonging to Tenant B, enforced via EF Core global query filters and PostgreSQL RLS.

## 2. Related Requirements
- User Story: US-CHR-001
- Non-Functional Requirements: NFR-2
- Business Rules: BR-1, BR-2

## 3. Preconditions
- Two tenants exist: "acme" (Tenant A) and "globex" (Tenant B), both with status `active`.
- Tenant A has 3 employees: "Alice A1", "Bob A2", "Carol A3".
- Tenant B has 2 employees: "Dave B1", "Eve B2".
- HR Officer users exist in both tenants.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme.yourhrm.com | 3 employees |
| Tenant B | globex.yourhrm.com | 2 employees |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate as HR Officer in Tenant A | Session established. |
| 2 | Send `GET /api/v1/tenant/employees` | Returns exactly 3 employees (Alice, Bob, Carol). None of Tenant B's employees appear. |
| 3 | Attempt to access a known Tenant B employee by ID: `GET /api/v1/tenant/employees/{daveB1_id}` | 404 Not Found. The global query filter prevents resolution. |
| 4 | Authenticate as HR Officer in Tenant B | Session established. |
| 5 | Send `GET /api/v1/tenant/employees` | Returns exactly 2 employees (Dave, Eve). None of Tenant A's employees appear. |
| 6 | Attempt to access a known Tenant A employee by ID: `GET /api/v1/tenant/employees/{aliceA1_id}` | 404 Not Found. |

## 6. Postconditions
- Complete data isolation between tenants is confirmed.
- No cross-tenant employee data leakage.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
