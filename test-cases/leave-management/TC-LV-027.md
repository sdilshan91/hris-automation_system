---
id: TC-LV-027
user_story: US-LV-002
module: Leave Management
priority: critical
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-027: Rule priority -- most specific rule wins when overlapping rules exist

## 1. Test Objective
Verify that when an employee matches multiple entitlement rules, the most specific rule wins according to the documented priority order: employee override > (department + job level + employment type) > (department + job level) > (department) > (job level) > default entitlement on leave type. This tests the conflict resolution engine described in FR-2 and AC-2.

## 2. Related Requirements
- User Story: US-LV-002
- Acceptance Criteria: AC-2
- Functional Requirements: FR-2
- Business Rules: BR-1

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with `Leave.Configure` permission is authenticated in the "acme" tenant context.
- Leave type "Annual Leave" exists with default entitlement of 14 days.
- Department "Engineering" exists.
- Job level "Senior" exists.
- Job title "Lead Engineer" exists.
- Employment type "Full-Time" is configured.
- Employee "Alice Chen" is in Engineering department, Senior level, Lead Engineer title, Full-Time.

## 4. Test Data
| Rule | Department | Job Level | Job Title | Employment Type | Days | Expected Priority |
|------|-----------|-----------|-----------|-----------------|------|-------------------|
| R1 (Default) | -- | -- | -- | -- | 14 | 6 (lowest) |
| R2 (Job Level only) | -- | Senior | -- | -- | 18 | 5 |
| R3 (Department only) | Engineering | -- | -- | -- | 20 | 4 |
| R4 (Dept + Level) | Engineering | Senior | -- | -- | 22 | 3 |
| R5 (Dept + Title) | Engineering | -- | Lead Engineer | -- | 23 | between 3 and 4 |
| R6 (Dept + Level + EmpType) | Engineering | Senior | -- | Full-Time | 25 | 2 |
| Employee | Alice Chen | Engineering, Senior, Lead Engineer, Full-Time | | | -- | -- |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Create Rule R1: Default entitlement for "Annual Leave" = 14 days (no department, no job level, no title, no employment type) | Rule saved. This is the fallback default. |
| 2 | Trigger accrual recalculation for Alice Chen | Alice receives 14 days (only default rule matches). |
| 3 | Create Rule R2: "Annual Leave" + Job Level "Senior" = 18 days | Rule saved. |
| 4 | Trigger accrual recalculation for Alice Chen | Alice now receives 18 days (job-level rule is more specific than default). |
| 5 | Create Rule R3: "Annual Leave" + Department "Engineering" = 20 days | Rule saved. |
| 6 | Trigger accrual recalculation for Alice Chen | Alice now receives 20 days (department rule is more specific than job-level-only). |
| 7 | Create Rule R4: "Annual Leave" + Department "Engineering" + Job Level "Senior" = 22 days | Rule saved. |
| 8 | Trigger accrual recalculation for Alice Chen | Alice now receives 22 days (department + level is more specific than department-only). |
| 9 | Create Rule R6: "Annual Leave" + Department "Engineering" + Job Level "Senior" + Employment Type "Full-Time" = 25 days | Rule saved. |
| 10 | Trigger accrual recalculation for Alice Chen | Alice now receives 25 days (department + level + employment type is most specific rule). |
| 11 | Verify an employee "Bob" in Engineering, Junior level, Full-Time receives 20 days | Bob matches R3 (department only) since no department + Junior rule exists. |
| 12 | Verify an employee "Carol" in Marketing, Senior level receives 18 days | Carol matches R2 (job level only) since no Marketing department rule exists. |
| 13 | Verify an employee "Dave" in Marketing, Junior level receives 14 days | Dave matches only R1 (default). |

## 6. Postconditions
- Multiple overlapping entitlement rules coexist without conflict.
- Each employee receives entitlement from the most specific matching rule.
- The specificity engine is documented and consistent across all calculations.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
