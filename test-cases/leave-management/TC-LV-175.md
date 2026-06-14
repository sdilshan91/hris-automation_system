---
id: TC-LV-175
user_story: US-LV-009
module: Leave Management
priority: critical
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-175: Manager scope is limited to direct reports -- other managers' team leaves are NOT shown (BR-2, Test Hint)

## 1. Test Objective
Verify a Manager's Team Leave Calendar includes only their own direct reports (resolved via `ReportsToEmployeeId`) and never surfaces leaves belonging to another manager's team within the same tenant.

## 2. Related Requirements
- User Story: US-LV-009
- Business Rules: BR-2
- Functional Requirements: FR-1, FR-2
- Non-Functional Requirements: NFR-3
- Test Hint: "Manager should not see leaves from other managers' teams."

## 3. Preconditions
- Tenant "acme" with two managers: Maya (direct reports Sam, Ravi) and Omar (direct report Lena).
- All have approved leaves in the viewed month.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Maya's reports | Sam, Ravi | should appear |
| Omar's report | Lena | must NOT appear in Maya's calendar |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Maya loads the Team Leave Calendar | Only Sam's and Ravi's leaves appear. |
| 2 | Search/scan for Lena | Lena (Omar's report) does NOT appear anywhere in Maya's calendar response. |
| 3 | Omar loads his calendar | Omar sees Lena only, not Sam/Ravi. |
| 4 | Maya attempts `?employeeId={Lena's id}` filter | The filter returns empty (Lena is out of scope); it does not escalate access to another team's data. |

## 6. Postconditions
- Each manager sees only their own direct reports; cross-team data is not exposed via default load or filter abuse.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
