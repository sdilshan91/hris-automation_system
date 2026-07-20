---
id: TC-CHR-338
user_story: US-CHR-011
module: Core HR
priority: medium
type: functional
status: automated
created: 2026-07-21
automated: 2026-07-21
defect:
  - DF-8
  - ISSUE-218
---

# TC-CHR-338: Reporting-chain breadcrumb — the ascending manager chain (Employee → Manager → … → root) is exposed via a dedicated endpoint, cycle-safe and tenant-scoped (DF-8 / ISSUE-218)

## 1. Test Objective
Verify the DF-8 / ISSUE-218 feature: a new `GET /api/v1/tenant/employees/{id}/reporting-chain`
returns the full **ascending** reporting chain for an employee — element 0 is the employee, then each
manager up `ReportsToEmployeeId` to the top (a root has no manager). Previously only the *immediate*
manager was exposed (on `EmployeeDto`/`EmployeeProfileDto`). The walk resolves in **one** projected
tenant-scoped query + one batched job-title lookup (no per-level N+1, no raw SQL/CTE), is **cycle-safe**
(a `HashSet` visited-set + `MaxChainDepth` cap), **truncates** cleanly at a missing/soft-deleted rung,
returns **404** for an unknown employee, and never crosses the **tenant** boundary.

## 2. Related Requirements
- User Story: US-CHR-011 (employee reporting structure)
- Business Rule: the reporting graph is a tenant-scoped self-referential hierarchy (`Employee.ReportsToEmployeeId`, nullable, ON DELETE SET NULL)
- Finding: DF-8 / ISSUE-218 (only the immediate manager was exposed; the full breadcrumb chain was not)
- Isolation: Critical Rule #1 (tenant isolation, BUG-003 class)

## 3. Preconditions
- `IReportingStructureService.GetReportingChainAsync` callable; the endpoint gated `[RequirePermission("Employee.View.All")]` (mirrors the sibling `direct-reports` action).
- Employees seeded through the real `AppDbContext` global query filter (InMemory-through-real-EF).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| 4-level graph | Employee → Manager → VP → CEO | CEO has `ReportsToEmployeeId == null` (root) |
| Root employee | `ReportsToEmployeeId == null` | single-element chain |
| Cyclic graph | A→B→A | visited-set terminates, bounded + unique |
| Missing rung | `ReportsToEmployeeId` = absent id | chain truncates at the employee |
| Foreign manager | manager owned by tenant B | must NOT appear in tenant A's chain |
| Unknown employee | random id | 404 |

## 5. Test Steps
| Step | Action | Expected Result | Automated by |
|------|--------|-----------------|--------------|
| 1 | Request the chain for the bottom employee of a 4-level hierarchy. | 4 elements in ascending order: `[Employee, Manager, VP, CEO]`, each with the right Name + JobTitle; CEO is terminal. | `GetReportingChain_FourLevelChain_ReturnsAscendingOrder_WithNamesAndTitles` |
| 2 | Request the chain for a root employee (no manager). | Single-element chain (just themselves). | `GetReportingChain_RootEmployee_ReturnsSingleElement` |
| 3 | Request the chain over a cyclic graph (A→B→A). | The walk terminates; the chain is bounded and de-duplicated (`HaveCount(2)` + unique ids) — the visited-set guard is load-bearing. | `GetReportingChain_CyclicGraph_Terminates_WithBoundedChain` |
| 4 | Request the chain where the manager id is absent from the tenant set. | Chain stops cleanly at the employee (single element), no throw. | `GetReportingChain_MissingRung_TruncatesChain_NoThrow` |
| 5 | Tenant A employee whose `ReportsToEmployeeId` points at a manager owned by **tenant B**. | Chain truncates at the boundary — a single element (the employee); tenant B's manager id **never** appears. | `GetReportingChain_ManagerInAnotherTenant_ChainTruncatesAtTheBoundary_NoLeak` |
| 6 | Request the chain for an unknown employee id. | `IsFailure`, `StatusCode == 404`, "Employee not found". | `GetReportingChain_UnknownEmployee_Returns404` |
| 7 | Request the chain with an unresolved tenant context. | `IsFailure`, "Tenant context is not resolved". | `GetReportingChain_TenantNotResolved_Fails` |

## 6. Postconditions
- The ascending reporting chain is retrievable per employee via a first-class endpoint; the walk is
  bounded, tenant-safe, and degrades gracefully on cycles / missing rungs / unknown ids.

## 7. Test Category Tags
- [x] Happy path (4-level + root)
- [x] Negative test (unknown → 404, unresolved tenant)
- [x] Boundary test (cycle, missing-rung truncation)
- [ ] Security test
- [x] Multi-tenant isolation (foreign manager never leaks)
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## Automation & Traceability
- **Automated-by (green in the xUnit suite), all carrying `[Trait("TC", "TC-CHR-338")]`:**
  - `HRM.Tests/Unit/ReportingStructureServiceTests.GetReportingChain_FourLevelChain_ReturnsAscendingOrder_WithNamesAndTitles`
  - `…GetReportingChain_RootEmployee_ReturnsSingleElement`
  - `…GetReportingChain_CyclicGraph_Terminates_WithBoundedChain`
  - `…GetReportingChain_MissingRung_TruncatesChain_NoThrow`
  - `…GetReportingChain_ManagerInAnotherTenant_ChainTruncatesAtTheBoundary_NoLeak`
  - `…GetReportingChain_UnknownEmployee_Returns404`
  - `…GetReportingChain_TenantNotResolved_Fails`
- Endpoint: `GET /api/v1/tenant/employees/{id}/reporting-chain` on `EmployeesController`, `[RequirePermission("Employee.View.All")]`.
