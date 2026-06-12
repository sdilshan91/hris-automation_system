---
id: TC-CHR-248
user_story: US-CHR-010
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-248: Plan limit pre-validation -- tenant near capacity, import partially allowed with user choice

## 1. Test Objective
Verify that when an import would exceed the tenant's employee plan limit, the system pre-validates the count and displays a warning with the exact numbers. The user can choose to import up to the remaining capacity or cancel entirely. This validates AC-5 and FR-9.

## 2. Related Requirements
- User Story: US-CHR-010
- Acceptance Criteria: AC-5
- Functional Requirements: FR-9

## 3. Preconditions
- Tenant "acme" exists with `max_employees = 50` (plan limit) and currently has 48 active employees.
- An HR Officer user is authenticated in the "acme" tenant context.
- The import file has 5 valid rows with all required fields correct.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | HR Officer | Authorized persona |
| Plan Limit | 50 | Tenant.MaxEmployees = 50 |
| Current Count | 48 | 48 active employees |
| File Name | five_employees.csv | 5 valid rows |
| Available Capacity | 2 | 50 - 48 = 2 slots |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Upload `five_employees.csv` and click "Import". | Before processing rows, the system performs a pre-validation count check. |
| 2 | Observe the pre-validation warning. | A warning dialog/banner appears: "This import would exceed your plan's employee limit (50). Only 2 of 5 records can be imported. Upgrade your plan or reduce the file." Two options are presented: "Import first 2" and "Cancel". |
| 3 | Click "Import first 2". | The system imports the first 2 valid rows from the file and skips the remaining 3. |
| 4 | Verify the results summary. | Summary shows: "2 of 5 records imported successfully. 3 records skipped (plan limit reached)." |
| 5 | Query the `employees` table. | 2 new employees created (the first 2 rows from the file). Total active employees = 50. |
| 6 | Verify audit log. | Import logged with: total = 5, imported = 2, skipped = 3, reason = "plan limit". |
| 7 | Repeat the test but click "Cancel" at step 2. | No employees are imported. The system returns to Step 2 (Upload). Employee count remains 48. |

## 6. Postconditions
- When "Import first 2" chosen: tenant has 50 employees (at plan limit).
- When "Cancel" chosen: no change to employee count.

## 7. Test Category Tags
- [x] Happy path
- [x] Boundary test
- [ ] Negative test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
