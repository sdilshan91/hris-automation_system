---
id: TC-CHR-ISO-047
user_story: US-CHR-012
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-13
---

# TC-CHR-ISO-047: RLS blocks direct DB queries across tenants for custom field definitions

## 1. Test Objective
Verify that the EF Core global query filter on the custom field definitions table filters by `tenant_id`, preventing any query from returning definitions belonging to a different tenant -- even if an application bug omits the tenant filter. This is the database-level defense layer for tenant isolation and validates NFR-2.

## 2. Related Requirements
- User Story: US-CHR-012
- Non-Functional Requirements: NFR-2
- Functional Requirements: FR-3

## 3. Preconditions
- Tenant "acme" and Tenant "globex" exist.
- Both tenants have custom field definitions.
- Access to the database or a test harness that can execute queries with controlled tenant context.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme | Has fields: T-Shirt Size, Project Code |
| Tenant B | globex | Has fields: Office Floor |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Set the tenant context to "acme". Execute `dbContext.CustomFieldDefinitions.ToListAsync()`. | Returns only acme's 2 definitions. No globex definitions appear. |
| 2 | Set the tenant context to "globex". Execute `dbContext.CustomFieldDefinitions.ToListAsync()`. | Returns only globex's 1 definition. No acme definitions appear. |
| 3 | Set the tenant context to "acme". Attempt to load globex's custom field by UUID: `dbContext.CustomFieldDefinitions.FindAsync(globex-field-id)`. | Returns null -- the global query filter excludes it even by direct ID lookup. |
| 4 | Execute a raw SQL query without tenant filter: `SELECT * FROM custom_field_definitions WHERE entity_type = 'employee'`. | If PostgreSQL RLS is configured, only the current tenant's rows are returned. If RLS is not configured, this step documents the gap and relies on the EF Core filter as the primary defense. |

## 6. Postconditions
- Database-level tenant isolation is confirmed for custom field definitions.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
