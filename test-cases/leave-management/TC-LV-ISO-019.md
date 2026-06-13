---
id: TC-LV-ISO-019
user_story: US-LV-005
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-13
---

# TC-LV-ISO-019: EF global query filters block cross-tenant access to leave_request, leave_approval_history, and leave_ledger rows during approval

## 1. Test Objective
Verify the read/write isolation layer for the approval flow: EF Core global query filters (the codebase's isolation mechanism in place of Postgres RLS, per the vault) prevent any cross-tenant access to `leave_request`, `leave_approval_history`, and `leave_ledger` rows when approving/rejecting. A query in Tenant A's context never returns or mutates Tenant B's rows.

## 2. Related Requirements
- User Story: US-LV-005
- Non-Functional Requirements: NFR-3
- Data Requirements (Section 7): leave_approval_history, leave_ledger

## 3. Preconditions
- Tenant "acme" and Tenant "globex" each have pending requests, approval-history rows, and ledger rows.
- The application enforces tenant isolation via EF Core global query filters + `TenantInterceptor` (NOT Postgres RLS), per `docs/vault/modules/leave-management.md`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| acme rows | leave_request, approval_history, ledger | tenant_id = acme |
| globex rows | leave_request, approval_history, ledger | tenant_id = globex |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | In acme's context, query `leave_request` for a globex request id | The global query filter returns no row; the request cannot be loaded for actioning. |
| 2 | In acme's context, approve an acme request | The new `leave_ledger` (`used`) and `leave_approval_history` rows are auto-stamped with `tenant_id = acme` by the `TenantInterceptor`. |
| 3 | In acme's context, query `leave_approval_history` and `leave_ledger` | Only acme rows are returned; globex approval-history and ledger rows are invisible. |
| 4 | Attempt a deliberate cross-tenant read only via `IgnoreQueryFilters()` in a controlled diagnostic | Confirms the filter (not raw table absence) is what scopes the data; production paths never call `IgnoreQueryFilters()` for these entities. |
| 5 | Confirm no globex rows were mutated by acme's approval | globex `leave_request`/`ledger`/`approval_history` are unchanged. |

## 6. Postconditions
- Cross-tenant reads/writes of approval-flow rows are blocked by EF global query filters; new rows are tenant-stamped.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
