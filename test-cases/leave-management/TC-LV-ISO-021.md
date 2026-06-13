---
id: TC-LV-ISO-021
user_story: US-LV-006
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-14
---

# TC-LV-ISO-021: Employee in Tenant A sees only their own balance data; Tenant B data is invisible

## 1. Test Objective
Verify cross-tenant isolation of the balance dashboard: an employee authenticated in Tenant A sees only their own tenant-scoped balances, ledger, and upcoming leaves, and cannot observe any Tenant B employee's data even with a known Tenant B identifier (NFR-3). (Test Hint: Employee in Tenant A must see only their own balance data.)

## 2. Related Requirements
- User Story: US-LV-006
- Non-Functional Requirements: NFR-3
- Functional Requirements: FR-1, FR-3, FR-4

## 3. Preconditions
- Tenant "acme" has employee "Nina Patel" with balances/ledger/upcoming data.
- Tenant "globex" has employee "Lara Voss" with balances/ledger/upcoming data.
- Nina is authenticated in the "acme" tenant context.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme | Nina Patel |
| Tenant B | globex | Lara Voss |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Nina (acme), call `my-balance`, `my-ledger`, `my-upcoming` | Returns only Nina's acme-scoped data. |
| 2 | Attempt to reference Lara's known globex employeeId / leaveTypeId in any parameter | Ignored / yields no rows; no globex balance, ledger, or upcoming data is returned. |
| 3 | Switch to globex (Lara) and load the dashboard (positive control) | Lara sees only her globex data; figures differ from Nina's. |
| 4 | Verify aggregate isolation | No acme response references globex rows and vice versa; balances/ledger remain strictly tenant-local. |

## 6. Postconditions
- Dashboard data is fully tenant-isolated; no cross-tenant leakage.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
