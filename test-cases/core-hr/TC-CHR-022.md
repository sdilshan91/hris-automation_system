---
id: TC-CHR-022
user_story: US-CHR-004
module: Core HR
priority: high
type: functional
status: automated
created: 2026-06-11
defect: BUG-013
automated_by:
  - HRM.Tests.Unit.DepartmentServiceTests.CreateDepartment_CaseVariantName_IsRejected_BUG013
  - HRM.Tests.Unit.DepartmentServiceTests.UpdateDepartment_CaseVariantOfAnother_IsRejected_BUG013
  - HRM.Tests.Unit.DepartmentServiceTests.CreateDepartment_GenuinelyDistinctName_Succeeds_BUG013
---

# TC-CHR-022: Duplicate name check is case-insensitive within tenant

## 1. Test Objective
Verify that the department name uniqueness constraint within a tenant is case-insensitive, so "Engineering" and "engineering" and "ENGINEERING" are treated as duplicates.

## 2. Related Requirements
- User Story: US-CHR-004
- Acceptance Criteria: AC-3
- Functional Requirements: FR-2
- Business Rules: BR-1

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with Tenant Admin role is authenticated in the "acme" tenant context.
- A department named "Engineering" exists in "acme".

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Existing Name | Engineering | Mixed case |
| Attempt 1 | engineering | All lowercase |
| Attempt 2 | ENGINEERING | All uppercase |
| Attempt 3 | eNgInEeRiNg | Alternating case |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `POST /api/v1/departments` with `{ name: "engineering" }` | Response status is 409 Conflict or 422. Error: "A department with this name already exists." |
| 2 | Send `POST /api/v1/departments` with `{ name: "ENGINEERING" }` | Response status is 409 Conflict or 422. Same error message. |
| 3 | Send `POST /api/v1/departments` with `{ name: "eNgInEeRiNg" }` | Response status is 409 Conflict or 422. Same error message. |
| 4 | Verify only one "Engineering" department exists in the database | No case-variant duplicates were created. |

## 6. Postconditions
- Only the original "Engineering" department exists.
- Case-insensitive uniqueness is enforced.

## Automated Coverage
Bound to the xUnit + EF Core InMemory unit suite (`HRM.Tests/Unit/DepartmentServiceTests.cs`).
The fix is an app-level `d.Name.ToLower() == name.Trim().ToLower()` comparison (BUG-013), which
InMemory evaluates faithfully — it does not depend on a DB unique index, so InMemory is appropriate.
- `CreateDepartment_CaseVariantName_IsRejected_BUG013` — create "engineering" over "Engineering" is rejected; only one row remains.
- `UpdateDepartment_CaseVariantOfAnother_IsRejected_BUG013` — rename to a case-variant of another department is rejected.
- `CreateDepartment_GenuinelyDistinctName_Succeeds_BUG013` — positive control: a distinct name still creates.

Pre-fix (case-sensitive `d.Name == name`) the case-variant slips the check and a second row persists, so
the "rejected" assertion fails. Post-fix they pass.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
