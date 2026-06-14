---
id: TC-LV-ISO-031
user_story: US-LV-008
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-14
---

# TC-LV-ISO-031: EF global query filters block cross-tenant access to carry-forward ledger and tracking rows (NFR-2, Section 7)

## 1. Test Objective
Verify read isolation for carry-forward data: the EF Core global query filters (the codebase's RLS-equivalent per docs/vault/modules/leave-management.md) ensure `carry_forward`/`expired` `leave_ledger` rows and `leave_carry_forward_tracking` rows are only visible within their owning tenant -- a query under Tenant B's context never returns Tenant A's carry-forward/expiry rows (NFR-2, Section 7).

## 2. Related Requirements
- User Story: US-LV-008
- Non-Functional Requirements: NFR-2
- Data Requirements: Section 7 (leave_ledger carry_forward/expired; leave_carry_forward_tracking)
- Note: Tenant isolation here is enforced by EF global query filters + TenantInterceptor (not Postgres RLS), per the module vault note.

## 3. Preconditions
- Tenant "acme" (Sam) and Tenant "globex" (Dana), each with carry-forward and expired ledger entries + tracking rows from a year-end run.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| acme carry_forward row | Sam, +5 | tenant A |
| globex carry_forward row | Dana, +8 | tenant B |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Under globex's tenant context, query leave_ledger carry_forward/expired rows | Only globex (Dana) rows are returned; acme (Sam) rows are filtered out by the global query filter. |
| 2 | Under globex's context, query `leave_carry_forward_tracking` | Only globex tracking rows are visible; acme tracking rows are absent. |
| 3 | Attempt to fetch an acme ledger/tracking row by its known id under globex's context | Not found / not returned -- the filter applies even to direct-id access. |
| 4 | Switch to acme's context and repeat | Symmetric: acme sees only its own carry-forward/expiry/tracking rows. |

## 6. Postconditions
- Carry-forward/expiry ledger and tracking rows are strictly tenant-scoped on read; no cross-tenant visibility.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Security test
- [ ] Boundary test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
