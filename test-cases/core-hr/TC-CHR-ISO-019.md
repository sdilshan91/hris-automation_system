---
id: TC-CHR-ISO-019
user_story: US-CHR-003
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-ISO-019: RLS blocks direct DB queries across tenants for directory data

## 1. Test Objective
Verify that PostgreSQL row-level security (via EF Core global query filters and explicit tenant_id conditions) prevents cross-tenant data access even if application-level protections were bypassed. This validates that the database layer enforces tenant isolation for directory queries including the "Show Archived" path that uses IgnoreQueryFilters.

## 2. Related Requirements
- User Story: US-CHR-003
- Functional Requirements: FR-7
- Non-Functional Requirements: NFR-3
- Business Rules: BR-1

## 3. Preconditions
- Tenant "acme" exists with employees: "Alice Adams", "Bob Baker".
- Tenant "globex" exists with employees: "Carol Chen", "Dave Daniels".
- Direct database access is available for verification.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| acme tenant_id | {acme_uuid} | |
| globex tenant_id | {globex_uuid} | |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Execute SQL query: `SELECT * FROM employees WHERE tenant_id = '{acme_uuid}'` | Returns only Alice Adams and Bob Baker. |
| 2 | Execute SQL query: `SELECT * FROM employees WHERE tenant_id = '{globex_uuid}'` | Returns only Carol Chen and Dave Daniels. |
| 3 | Verify EF Core global query filter | Inspect `AppDbContext.OnModelCreating` -- Employee entity has `HasQueryFilter(e => !e.IsDeleted && (!_tenantContext.IsResolved \|\| e.TenantId == _tenantContext.TenantId))`. |
| 4 | Test the "Show Archived" code path | When `showArchived=true`, the service uses `IgnoreQueryFilters()` but explicitly adds `WHERE tenant_id = @tenantId`. Verify this SQL is generated. |
| 5 | Verify no query can return employees without tenant_id filter | Inspect generated SQL logs; every directory query includes `tenant_id` condition. |

## 6. Postconditions
- No cross-tenant data was accessible.
- The "Show Archived" path maintains tenant isolation despite IgnoreQueryFilters.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
