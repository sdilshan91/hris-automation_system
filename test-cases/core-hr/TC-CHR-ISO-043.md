---
id: TC-CHR-ISO-043
user_story: US-CHR-011
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-ISO-043: RLS blocks direct DB queries across tenants for reporting structure data

## 1. Test Objective
Verify that PostgreSQL Row-Level Security (RLS) and EF Core global query filters prevent cross-tenant access to reporting structure data even if the application query accidentally omits the tenant filter. This validates NFR-3 and FR-9 at the database level.

## 2. Related Requirements
- User Story: US-CHR-011
- Non-Functional Requirements: NFR-3
- Functional Requirements: FR-9

## 3. Preconditions
- Tenant "acme" has employees with reporting structure (Manager A with direct reports E1, E2).
- Tenant "globex" has employees with reporting structure (Manager G with direct report G1).
- Direct database access is available for verification.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme | Manager A -> E1, E2 |
| Tenant B | globex | Manager G -> G1 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Using EF Core context with Tenant A's context: query all employees where `reports_to_employee_id` is not null. | Returns only Tenant A's employees (E1, E2). Tenant B's employees are excluded by the global query filter. |
| 2 | Using EF Core context with Tenant B's context: query all employees where `reports_to_employee_id` is not null. | Returns only Tenant B's employees (G1). |
| 3 | Using EF Core context with Tenant A's context: query employee by Manager G's UUID. | Returns null/empty (Manager G's UUID filtered out by tenant scope). |
| 4 | Verify via direct SQL (as the application DB user, NOT as superuser): `SELECT * FROM employees WHERE reports_to_employee_id IS NOT NULL` with Tenant A's RLS context. | Only Tenant A rows returned. |
| 5 | Attempt to assign a Tenant B employee UUID as manager via Tenant A's context. | The target manager UUID is not found (global filter excludes it). Assignment fails with 404 or "employee not found." |

## 6. Postconditions
- No cross-tenant data access. RLS and global query filters confirmed effective.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
