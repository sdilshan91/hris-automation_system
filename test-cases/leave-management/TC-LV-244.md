---
id: TC-LV-244
user_story: US-LV-012
module: Leave Management
priority: medium
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-244: Remaining pre-built reports available — Carry-Forward Summary, LOP Summary, Department Calendar Coverage (FR-1)

## 1. Test Objective
Verify FR-1: beyond the four AC-covered reports, the additional pre-built reports — Carry-Forward Summary, LOP Summary, and Department Leave Calendar Coverage — are available from the reports landing grid and return tenant-scoped data.

## 2. Related Requirements
- User Story: US-LV-012
- Functional Requirements: FR-1, FR-6
- Cross-ref: US-LV-008 (carry-forward), US-LV-011 (LOP), US-LV-009 (calendar)

## 3. Preconditions
- Tenant "acme"; HR authenticated; data with carry-forward ledger entries (US-LV-008), LOP entries (US-LV-011), and approved leaves for calendar coverage (US-LV-009).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| carry-forward | `/api/v1/leaves/reports/carry-forward-summary` | FR-1 |
| lop | `/api/v1/leaves/reports/lop-summary` | FR-1 (cross-ref US-LV-011 payroll lop-summary) |
| coverage | `/api/v1/leaves/reports/calendar-coverage` | FR-1 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Open the reports landing grid | Cards for Carry-Forward Summary, LOP Summary, and Department Leave Calendar Coverage are listed alongside the four primary reports, each with icon/description. |
| 2 | Open Carry-Forward Summary | Per-employee carried-forward / expired amounts (US-LV-008 ledger) are listed, tenant-scoped. |
| 3 | Open LOP Summary | Per-employee LOP day totals over the range are listed (consistent with the US-LV-011 lop-summary payload). |
| 4 | Open Department Leave Calendar Coverage | Coverage / on-leave headcount per department/date is shown for planning. |

## 6. Postconditions
- The full FR-1 pre-built report set is available and returns tenant-scoped data.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
