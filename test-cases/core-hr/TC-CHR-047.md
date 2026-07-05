---
id: TC-CHR-047
user_story: US-CHR-005
module: Core HR
priority: high
type: functional
status: automated
created: 2026-06-12
defect: BUG-016
automated_by:
  - HRM.Tests.Unit.JobTitleServiceTests.CreateJobTitle_CaseVariantName_IsRejected_BUG016
  - HRM.Tests.Unit.JobTitleServiceTests.UpdateJobTitle_CaseVariantOfAnother_IsRejected_BUG016
  - HRM.Tests.Unit.JobTitleServiceTests.CreateJobTitle_GenuinelyDistinctName_Succeeds_BUG016
---

# TC-CHR-047: Duplicate title name check is case-insensitive within tenant

## 1. Test Objective
Verify that the uniqueness constraint on `title_name` within a tenant is case-insensitive, so that "Software Engineer", "software engineer", and "SOFTWARE ENGINEER" are all treated as duplicates of the same name.

## 2. Related Requirements
- User Story: US-CHR-005
- Acceptance Criteria: AC-3
- Functional Requirements: FR-2
- Business Rules: BR-1

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with Tenant Admin role is authenticated in the "acme" tenant context.
- A job title named "Software Engineer" exists in the "acme" tenant.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| Existing Title | Software Engineer | Already exists |
| Attempt 1 | software engineer | Lowercase variant |
| Attempt 2 | SOFTWARE ENGINEER | Uppercase variant |
| Attempt 3 | Software  Engineer | Extra space in the middle |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Attempt to create a job title with "software engineer" (all lowercase) | Response status is 409 Conflict. Error message: "A job title with this name already exists." |
| 2 | Attempt to create a job title with "SOFTWARE ENGINEER" (all uppercase) | Response status is 409 Conflict. Error message: "A job title with this name already exists." |
| 3 | Attempt to create a job title with "Software  Engineer" (extra internal space) | Either rejected as duplicate (if the system normalizes whitespace) or accepted as a distinct name (implementation-dependent). Document actual behavior. |
| 4 | Verify no duplicate records were created for attempts 1 and 2 | Only the original "Software Engineer" exists in the database. |

## 6. Postconditions
- The uniqueness constraint prevents case-variant duplicates.
- Only the original "Software Engineer" record exists.

## Automated Coverage
Bound to the xUnit + EF Core InMemory unit suite (`HRM.Tests/Unit/JobTitleServiceTests.cs`).
The fix is an app-level `j.TitleName.ToLower() == titleName.Trim().ToLower()` comparison (BUG-016),
faithfully evaluated by InMemory (no DB unique index dependency).
- `CreateJobTitle_CaseVariantName_IsRejected_BUG016` — create "software engineer" over "Software Engineer" is rejected; only one row remains.
- `UpdateJobTitle_CaseVariantOfAnother_IsRejected_BUG016` — rename to a case-variant of another title is rejected.
- `CreateJobTitle_GenuinelyDistinctName_Succeeds_BUG016` — positive control: a distinct name still creates.

Pre-fix (case-sensitive `j.TitleName == titleName`) the case-variant slips the check and a second row
persists, so the "rejected" assertion fails. Post-fix they pass.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
