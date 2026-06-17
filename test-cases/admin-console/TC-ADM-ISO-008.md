---
id: TC-ADM-ISO-008
user_story: US-ADM-004
module: Admin Console
priority: critical
type: security
status: draft
created: 2026-06-17
---

# TC-ADM-ISO-008: Tenant isolation during deletion — deleting Tenant A leaves Tenant B's data completely unaffected

## 1. Test Objective
Verify AC-4 / Test-Hint tenant isolation: when the data-deletion job hard-deletes Tenant A's per-tenant data, Tenant B's data is entirely untouched. The deletion is scoped by `tenant_id` and rides the same EF Core tenant-scoping discipline; no cross-tenant rows are collaterally deleted.

## 2. Related Requirements
- User Story: US-ADM-004
- Acceptance Criteria: AC-4
- Functional Requirements: FR-3
- Test Hints: Tenant isolation during deletion (run queries against Tenant B before and after)

## 3. Preconditions
- Two non-system tenants: Acme (A, `Terminating`, populated) and Beta (B, `Active`, populated with employees/leave/attendance/payroll/settings).
- Known row counts and a few known record IDs for Beta captured BEFORE deletion.
- NOTE (platform accuracy): isolation is enforced by EF Core global query filters (read) + `TenantInterceptor` (write stamping); the deletion job filters explicitly by `tenant_id`. Postgres RLS is a deferred extension point — tests assert the EF/explicit-filter mechanism in force.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| tenant A (deleted) | Acme | Terminating |
| tenant B (untouched) | Beta | Active |
| Beta baseline | row counts + sample IDs | captured pre-deletion |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Snapshot Beta's per-tenant table row counts and several known record IDs | Baseline recorded. |
| 2 | Run the Acme data-deletion job to completion | Acme business data is gone; Acme tenant row retained as `Terminated` (TC-ADM-004-09). |
| 3 | Re-query Beta's per-tenant tables for the same counts and IDs | Identical to baseline — zero Beta rows deleted or altered. |
| 4 | Log in to Beta and exercise reads/writes | Beta operates normally; no orphaned references; no errors from the neighbor's deletion. |
| 5 | Inspect audit | The deletion audit references Acme only; no Beta records appear in the deletion scope. |

## 6. Postconditions
- Tenant A's deletion is perfectly tenant-confined; Tenant B is byte-for-byte unaffected.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
