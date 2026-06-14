---
id: TC-LV-ISO-029
user_story: US-LV-008
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-14
---

# TC-LV-ISO-029: Year-end job processes Tenant A and Tenant B independently -- no cross-tenant contamination (NFR-2; Test Hint)

## 1. Test Objective
Verify the carry-forward/expiry jobs are tenant-isolated: when the job iterates over active tenants, it restores tenant context per iteration and processes each tenant's employees against that tenant's own leave-type config, so Tenant A's carry-forward limits/balances never bleed into Tenant B's ledger entries (NFR-2, FR-4; Test Hint).

## 2. Related Requirements
- User Story: US-LV-008
- Non-Functional Requirements: NFR-2
- Functional Requirements: FR-4 (restore tenant context, process each tenant independently)
- Test Hint: Section 11 (Tenant A and Tenant B processed independently)

## 3. Preconditions
- Tenant "acme": Annual Leave `carry_forward_limit = 5`; employee "Sam" with 8 unused days.
- Tenant "globex": Annual Leave `carry_forward_limit = 10`; employee "Dana" with 8 unused days.
- Both tenants active; both have 2026 year-end balances.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| acme limit | 5 | Sam -> 5 cf / 3 expired |
| globex limit | 10 | Dana -> 8 cf / 0 expired |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Run `ProcessLeaveYearEndJob` for the year-end across both tenants | Sam (acme) gets +5 carry_forward / -3 expired using acme's limit of 5. |
| 2 | Inspect Dana (globex) | Dana gets +8 carry_forward / 0 expired using globex's limit of 10 -- acme's limit of 5 is NOT applied to globex. |
| 3 | Verify ledger ownership | Every carry_forward/expired entry carries the correct `TenantId`; no acme entry is written under globex (or vice versa). |
| 4 | Verify per-tenant context restoration | The job set the correct tenant context for each iteration (acme's config never resolves while processing globex). |

## 6. Postconditions
- Each tenant's year-end processing uses only its own config; ledger entries are tenant-correct with no cross-contamination.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Security test
- [ ] Boundary test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
