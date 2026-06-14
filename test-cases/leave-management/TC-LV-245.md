---
id: TC-LV-245
user_story: US-LV-012
module: Leave Management
priority: critical
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-245: Role-based access — HR sees all tenant employees in reports (BR-2, Test Hint)

## 1. Test Objective
Verify BR-2 (HR branch): a user with `Leave.Reports`/`HR.Officer` sees data for all employees across the tenant in every report, not limited to a team or to themselves.

## 2. Related Requirements
- User Story: US-LV-012
- Business Rules: BR-2
- Acceptance Criteria: AC-1, AC-2, AC-3

## 3. Preconditions
- Tenant "acme"; HR Officer authenticated; employees across multiple departments and managers.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Role | HR Officer / Leave.Reports | full-tenant scope |
| Employees | all departments | expected visible |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As HR, run the Balance Summary report with no filter | Every active employee in the tenant appears, regardless of department or reporting manager. |
| 2 | Run the Utilization and Absenteeism reports | Aggregations span the whole tenant org (all departments contribute). |
| 3 | Confirm no team/self restriction is applied for the HR role | The HR result set is a strict superset of any single manager's team or any single employee's own data (cross-ref TC-LV-246, TC-LV-247). |
| 4 | Confirm tenant boundary still holds | HR sees all of tenant A but no tenant B rows (cross-ref TC-LV-ISO-045). |

## 6. Postconditions
- HR/Leave.Reports users see all tenant employees in reports (full-tenant scope), still tenant-bounded.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
