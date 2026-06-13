---
id: TC-LV-ISO-023
user_story: US-LV-006
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-14
---

# TC-LV-ISO-023: EF global query filters block cross-tenant access to leave_ledger and leave_request rows

## 1. Test Objective
Verify the read-isolation layer for the dashboard's source tables: EF Core global query filters scope `leave_ledger`, `leave_request`, and `leave_types` reads to the resolved tenant, so a balance/ledger/upcoming query in Tenant A cannot read Tenant B rows even with a known primary key (NFR-3, Section 7). (Per vault: isolation is enforced via EF global query filters + TenantInterceptor, the RLS-equivalent in this codebase.)

## 2. Related Requirements
- User Story: US-LV-006
- Non-Functional Requirements: NFR-3
- Data Requirements (Section 7): leave_ledger, leave_request
- Note: NFR-3 says "PostgreSQL RLS"; this codebase enforces the equivalent via EF Core global query filters + TenantInterceptor (see docs/vault/modules/leave-management.md).

## 3. Preconditions
- Tenant "acme" (Nina) and Tenant "globex" (Lara) each have `leave_ledger` and `leave_request` rows with known IDs.
- Nina is authenticated in the acme context.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| globex ledger row id | known UUID | Cross-tenant target |
| globex request id | known UUID | Cross-tenant target |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Nina (acme), exercise `my-balance`/`my-ledger`/`my-upcoming` | Underlying queries include the `TenantId == acme` global filter; only acme rows are aggregated. |
| 2 | Attempt to retrieve a known globex `leave_ledger` row id via any reachable path | The row is invisible (filtered out); no globex ledger amount contributes to any acme balance. |
| 3 | Attempt to surface a known globex `leave_request` in Upcoming/History | Not returned; the global filter excludes it. |
| 4 | Confirm the filter is not bypassed | No `IgnoreQueryFilters()` is used on the employee-facing dashboard read paths; cross-tenant rows never appear. |

## 6. Postconditions
- Source-table reads are tenant-filtered; cross-tenant rows are unreachable from the dashboard.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
