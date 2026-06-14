---
id: TC-LV-ISO-043
user_story: US-LV-011
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-14
---

# TC-LV-ISO-043: EF Core global query filters block cross-tenant leave_request / leave_ledger access during LOP operations (NFR-2)

## 1. Test Objective
Verify at the data-access layer that the EF Core global query filters (the codebase's RLS-equivalent per the vault) prevent any LOP read or write from touching another tenant's `leave_request` / `leave_ledger` rows: a direct query by id for a Tenant B LOP row, issued under Tenant A's context, returns nothing and cannot be mutated.

## 2. Related Requirements
- User Story: US-LV-011
- Non-Functional Requirements: NFR-2
- Data §7 (leave_request is_lop/lop_source; compulsory_leave)
- Note: the story says "PostgreSQL RLS"; this codebase enforces isolation via EF global query filters + TenantInterceptor (per docs/vault/modules/leave-management.md).

## 3. Preconditions
- Tenant "acme" (employee Mark with an LOP `leave_request` + `leave_ledger` entry) and Tenant "globex".

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| acme LOP row id | known | tenant A |
| Query context | globex | tenant B |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Under globex's tenant context, query the acme LOP `leave_request` by its known id | Returns no row — the global query filter (`TenantId == _tenantContext.TenantId`) excludes it. |
| 2 | Under globex's context, query the acme LOP `leave_ledger` entry by id | Returns no row; cross-tenant ledger access blocked. |
| 3 | Attempt to update/override the acme LOP row from globex's context | The row is not materialized under globex, so no update applies; acme's row is unchanged. |
| 4 | Confirm `IgnoreQueryFilters()` is NOT used on the LOP read/write paths | The LOP service paths rely on the default filtered DbSets (no deliberate filter bypass that would leak tenants). |

## 6. Postconditions
- EF global query filters block cross-tenant LOP leave_request/leave_ledger access at the data layer.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Security test
- [ ] Boundary test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
