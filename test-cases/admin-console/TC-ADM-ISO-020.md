---
id: TC-ADM-ISO-020
user_story: US-ADM-007
module: Admin Console
priority: medium
type: security
status: blocked
created: 2026-06-17
---

# TC-ADM-ISO-020: [DEFERRED] PostgreSQL RLS DB-layer isolation for workflow definitions

## 1. Test Objective
Verify a DB-layer (PostgreSQL Row-Level Security) isolation guarantee for `WorkflowDefinition`/`WorkflowStep`: a raw SQL query without the tenant session context returns zero rows, independent of the application/EF layer.

## 2. Related Requirements
- User Story: US-ADM-007
- Functional Requirements: FR-7 (tenant-scoped)
- Business Rules: BR-7
- Note: this is the same deferred-platform RLS family as US-ADM-001..006 / Payroll / Leave.

## 3. Preconditions (NOT satisfiable in the current build)
- The platform enforces tenant isolation via the APP layer (`ITenantContext`) + EF layer (global query filter on read, `TenantInterceptor` on write). PostgreSQL RLS is NOT enabled — it is a deferred platform extension point.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| raw query | SELECT * FROM workflow_definition (no tenant session var) | |

## 5. Test Steps (deferred — to execute once RLS lands)
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Run a raw SQL read against `workflow_definition` without the tenant session context | (When RLS lands) returns zero rows — DB-layer policy blocks cross-tenant reads even bypassing EF. |
| 2 | Set the tenant session context, re-run | Returns only that tenant's rows. |

## 6. Postconditions
- DEFERRED. Today, app + EF query-filter isolation is the in-force, run-green mechanism (TC-ADM-ISO-017/-018/-019). RLS is future hardening.

## 7. Status / Deferral Note
- status: blocked. PostgreSQL RLS is not enabled on this platform; EF query filters + TenantInterceptor are the active isolation layers. STORY MISMATCH to flag: any story text that assumes RLS as the active DB-layer guarantee should be reworded with RLS as future hardening. Never fabricated as pass.

## 8. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
