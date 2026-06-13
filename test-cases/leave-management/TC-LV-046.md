---
id: TC-LV-046
user_story: US-LV-002
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-046: Job-level dimension in entitlement rules (DEFERRED)

## 1. Test Objective
Verify that entitlement rules support the job-level dimension as a rule criteria alongside department, job title, and employment type. An entitlement rule scoped to a specific job level (e.g., "Senior") applies to all employees at that level regardless of department.

**DEFERRED:** This test case covers the `job_level_id` dimension on entitlement rules. The `leave_entitlement_rule` table schema includes `job_level_id (uuid FK, nullable)`, but the actual implementation of job levels as a separate entity (distinct from job titles) may be pending. If the job level entity exists and is linked to employees, this test should be activated. The related tenure bracket dimension (`tenure_min_months`, `tenure_max_months` from FR-1) is also deferred pending implementation.

## 2. Related Requirements
- User Story: US-LV-002
- Functional Requirements: FR-1
- Data Requirements: Section 7

## 3. Preconditions
- **DEFERRED** -- Job Level entity must exist separately from Job Title.
- Tenant "acme" exists with job levels: Junior, Mid, Senior, Principal.
- Leave type "Annual Leave" exists and is active.

## 4. Test Data
| Job Level | Entitlement (Annual Leave) |
|-----------|---------------------------|
| Junior | 15 days |
| Mid | 18 days |
| Senior | 22 days |
| Principal | 25 days |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | **DEFERRED** -- Create entitlement rule: Annual Leave + Job Level "Junior" = 15 days | Rule saved with job_level_id populated, department_id = null. |
| 2 | **DEFERRED** -- Create rules for Mid (18), Senior (22), Principal (25) | All rules saved. |
| 3 | **DEFERRED** -- Trigger accrual for a Junior employee in any department | Balance = 15 days. |
| 4 | **DEFERRED** -- Trigger accrual for a Senior employee in any department | Balance = 22 days. |
| 5 | **DEFERRED** -- Create a department-specific rule for Engineering = 20 days | Conflict: Junior in Engineering -- should get 20 (dept) or 15 (level)? Verify specificity: department > job-level-only per FR-2. |
| 6 | **DEFERRED** -- Verify tenure bracket filtering: rule with tenure_min_months = 60, tenure_max_months = null applies only to employees with 5+ years tenure | Tenure brackets correctly filter rule applicability. |

## 6. Postconditions
- **DEFERRED** -- Job level dimension works as a standalone and combined rule criteria.
- Specificity resolution handles job-level-only vs department-only correctly per FR-2.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
