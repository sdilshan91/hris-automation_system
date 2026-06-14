---
id: TC-LV-ISO-039
user_story: US-LV-010
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-14
---

# TC-LV-ISO-039: EF Core global query filters block cross-tenant leave_request / leave_ledger access during cancellation (NFR-2, Section 7)

## 1. Test Objective
Verify that the EF Core global query filters (the codebase's RLS-equivalent per docs/vault/modules/leave-management.md) prevent the cancellation handler from reading, updating, or writing a reversal ledger entry against another tenant's `leave_request` / `leave_ledger` rows, regardless of the request id supplied. The story's literal "PostgreSQL RLS" (NFR-2) is met by the EF global-filter + TenantInterceptor mechanism.

## 2. Related Requirements
- User Story: US-LV-010
- Non-Functional Requirements: NFR-2
- Data Requirements: Section 7
- Note: Tenant isolation is EF global query filters + TenantInterceptor, NOT Postgres RLS (per vault). ISO TCs describe the EF-filter mechanism.

## 3. Preconditions
- Tenant "acme" (Jane, request R-acme) and Tenant "globex" (Kofi, request R-globex), each with their own `leave_ledger` rows.
- Tenant context resolved to acme for the acting session.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| acme rows | R-acme + Jane's ledger | TenantId=acme |
| globex rows | R-globex + Kofi's ledger | TenantId=globex |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | With acme context, the cancel handler loads the target request by id `{R-globex}` | The global query filter (`TenantId == acme`) excludes R-globex; the lookup returns no row -> handled as not-found; no update issued. |
| 2 | Attempt a reversal ledger write scoped to acme referencing globex's employee/leave-type | The TenantInterceptor stamps `TenantId = acme` on any new row; a reversal cannot be attributed to globex's tenant, so globex's balance is never altered. |
| 3 | Query globex's `leave_request` and `leave_ledger` directly | R-globex is still Approved; no acme-originated reversal `adjusted` row exists under globex. |
| 4 | Confirm the filter is not bypassed | No `IgnoreQueryFilters()` is used on the cancellation path; cross-tenant rows remain invisible. |

## 6. Postconditions
- The EF global query filter + TenantInterceptor block all cross-tenant read/update/ledger-write during cancellation; each tenant's rows stay isolated.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Security test
- [ ] Boundary test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
