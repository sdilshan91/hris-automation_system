---
id: TC-LV-216
user_story: US-LV-011
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-216: assign-lop accepts multiple non-contiguous dates and validates them (boundary / input validation)

## 1. Test Objective
Verify `POST /api/v1/leaves/assign-lop` correctly handles the `dates[]` array boundary cases: multiple non-contiguous dates each create the right LOP day count, an empty `dates[]` is rejected, and a duplicate date within the array is de-duplicated (not double-counted), so the LOP day total fed to payroll is accurate (AC-3, FR-3).

## 2. Related Requirements
- User Story: US-LV-011
- Acceptance Criteria: AC-3
- Functional Requirements: FR-3, FR-4

## 3. Preconditions
- Tenant "acme"; LOP type exists; employee "Mark Otieno".
- HR Officer "Asha" authenticated with assign-LOP permission.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| dates[] (A) | 3 non-contiguous days | total LOP = 3 |
| dates[] (B) | [] empty | invalid |
| dates[] (C) | [D, D] duplicate | should count as 1 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | assign-lop with 3 non-contiguous dates | LOP request(s)/entries totalling 3 LOP days created; the lop-summary total for the period = 3. |
| 2 | assign-lop with an empty `dates[]` | 400 validation error ("at least one date required"); nothing persisted. |
| 3 | assign-lop with a duplicated date | The duplicate is collapsed to a single LOP day (total = 1), preventing an inflated payroll deduction. |
| 4 | assign-lop with a malformed/non-date value | 400 validation error; no partial persistence. |

## 6. Postconditions
- The assign-lop `dates[]` array is validated; the resulting LOP day count is accurate (no empty/duplicate/malformed inflation).

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
