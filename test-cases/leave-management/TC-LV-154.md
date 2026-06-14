---
id: TC-LV-154
user_story: US-LV-008
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-154: Encashable leave type encashes forfeitable balance instead of expiring it (BR-5; CONDITIONAL on leave-type config)

## 1. Test Objective
Verify that for a leave type configured as `encashable = true`, the year-end job may encash the forfeitable (excess) balance as an `Encashed` ledger transaction instead of expiring it, when the encashment-on-expiry config is enabled (BR-5). This behavior is CONDITIONAL on the leave-type configuration (encashable + max encash days).

## 2. Related Requirements
- User Story: US-LV-008
- Business Rules: BR-5
- Cross-reference: US-LV-001 (leave type `encashable`, `max_encash_days`), US-LV-002 (LeaveLedger `Encashed` entry type)
- Note: Encashment-on-expiry is driven by leave-type config; if the config flag is absent, the excess expires (TC-LV-150 path). Mark CONDITIONAL accordingly.

## 3. Preconditions
- Tenant "acme" with an encashable leave type ("Annual Leave (Encashable)"): `encashable = true`, `max_encash_days = 5`, `carry_forward_limit = 5`.
- Employee "Sam" ends 2026 with 8 unused days (5 carry-forward eligible, 3 excess/forfeitable).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Unused balance | 8 days | -- |
| carry_forward_limit | 5 | carried |
| Forfeitable excess | 3 days | candidate for encashment |
| max_encash_days | 5 | cap on encashment |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | With encashment-on-expiry enabled, run `ProcessLeaveYearEndJob` | 5 days carried forward; the 3 forfeitable days are recorded as an `Encashed` ledger entry (not `expired`) within the `max_encash_days` cap (BR-5). |
| 2 | Verify no expiry occurs for the encashed portion | No `expired` ledger entry is created for the 3 encashed days. |
| 3 | Disable the encashment-on-expiry config and re-run on a fresh fixture | The 3 excess days are `expired` instead (default forfeiture path, cross-ref TC-LV-150) -- confirming the behavior is config-driven (CONDITIONAL). |
| 4 | Boundary -- excess exceeds max_encash_days | If forfeitable excess > `max_encash_days`, only up to the cap is encashed and the remainder expires. |

## 6. Postconditions
- For an encashable type with encashment-on-expiry enabled, forfeitable balance is encashed up to the cap; otherwise it expires (config-driven).

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
