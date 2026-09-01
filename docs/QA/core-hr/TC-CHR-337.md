---
id: TC-CHR-337
user_story: US-CHR-005
module: Core HR
priority: high
type: functional
status: pass
created: 2026-07-20
defect:
  - ISSUE-021
---

# TC-CHR-337: SalaryGrade CRUD (code uniqueness 409, Min≤Mid≤Max ordering 422, deactivate, cross-tenant 404) and JobTitle.GradeId FK-validated against a real active in-tenant SalaryGrade (arbitrary/inactive/cross-tenant rejected, valid accepted) — US-CHR-005 AC-4 / FR-3 (#389, ISSUE-021)

## 1. Test Objective
Verify the #389 SalaryGrade entity + the US-CHR-005 grade-linking contract (ISSUE-021). The `SalaryGrade` is now a real tenant-scoped entity with full CRUD: create trims code + uppercases currency and persists tenant-stamped; a **duplicate code** within a tenant (case-insensitive) is rejected **409 `duplicate_code`**; an invalid amount range (Min > Max, or Mid outside [Min,Max]) is rejected **422 `invalid_amount_range`**; deactivate is a soft-delete that hides the grade from the active list; reads/lists are tenant-scoped (`GetById` cross-tenant → **404**, `GetAll` returns only the current tenant's grades, and the same code may coexist across tenants). On the JobTitle side (US-CHR-005 AC-4 / FR-3), `JobTitle.GradeId` is now **FK-validated**: a non-null grade id must resolve to a **real, active, in-tenant** `SalaryGrade` — an arbitrary/unseeded id, a **deactivated** grade, and a **cross-tenant** grade are all rejected **`invalid_grade`**, a valid seeded active id is accepted, a null grade id is allowed (BR-2), and the linked grade's NAME is populated on the read DTO.

## 2. Related Requirements
- User Story: US-CHR-005
- Acceptance Criteria: AC-4 (linking a job title to a salary grade sets the FK, FK-validated against a real active in-tenant `SalaryGrade` (#389); may be left null)
- Functional Requirement: FR-3 (grade link is a nullable FK, FK-validated against an active in-tenant grade; unresolvable references rejected)
- Business Rules: BR-2 (a job title can exist without a grade), BR-5 (grades are tenant-specific)
- Related: US-PAY-001 AC-K1 (the `SalaryGrade` entity was delivered under #389)
- Finding: ISSUE-021 (SalaryGrade management + JobTitle.GradeId FK validation)

## 3. Preconditions
- A tenant with the real `SalaryGradeService` / `JobTitleService` over EF Core InMemory-through-real-EF (the tenant global query filter is exercised); a second tenant context for the cross-tenant arms.
- This TC unblocks the previously-blocked **TC-CHR-063** (grade-on-profile), whose SalaryGrade dependency is now shipped.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Grade code | "G1" (+ "  g2  ", "g1") | trim + case-insensitive uniqueness |
| Amounts (valid) | Min 1000 ≤ Mid 1500 ≤ Max 2000 | ordering ok |
| Amounts (invalid) | Min 3000 > Max 2000; Mid 5000 ∉ [1000,2000] | 422 `invalid_amount_range` |
| JobTitle grade (reject) | unseeded id / inactive grade / other-tenant grade | `invalid_grade` |
| JobTitle grade (accept) | seeded active in-tenant grade; or null | accepted (null ok, BR-2) |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Create a valid grade; create with padded code + lowercase currency; create with null mid. | Persisted tenant-stamped; code trimmed, currency uppercased; null mid allowed. `Create_ValidGrade_PersistsAndReadsBack`, `Create_TrimsCode_AndUppercasesCurrency`, `Create_NullMidAmount_IsAllowed`. |
| 2 | Create a duplicate code (same case / case-variant). | 409 `duplicate_code`. `Create_DuplicateCodeSameTenant_ShouldFail_409`, `Create_DuplicateCodeCaseVariant_ShouldFail_409`. |
| 3 | Create with Min > Max / Mid outside range (and same on Update). | 422 `invalid_amount_range`. `Create_MinGreaterThanMax_ShouldFail_422`, `Create_MidOutsideRange_ShouldFail_422`, `Update_MinGreaterThanMax_ShouldFail_422`. |
| 4 | Update valid change / non-existent / duplicate-excluding-self / same-code-as-self. | Success; 404; 409; success. `Update_ValidChange_ShouldSucceed`, `Update_NonExistent_ShouldReturn404`, `Update_DuplicateCodeExcludingSelf_ShouldFail_409`, `Update_SameCodeAsSelf_ShouldSucceed`. |
| 5 | Deactivate a grade; deactivate again; deactivate non-existent. | Hidden from active list (visible with includeInactive); "already deactivated"; 404. `Deactivate_HidesFromActiveList`, `Deactivate_AlreadyInactive_ShouldFail`, `Deactivate_NonExistent_ShouldReturn404`. |
| 6 | Read by id; list default (excludes inactive); cross-tenant reads/creates. | Grade returned; inactive excluded; `GetAll`/`GetById` tenant-scoped (cross-tenant 404); same code across tenants allowed; unresolved-tenant fails. `GetById_ReturnsGrade`, `GetAll_DefaultExcludesInactive`, `GetAll_ShouldOnlyReturnCurrentTenantGrades`, `GetById_CrossTenant_ShouldReturn404`, `Create_SameCodeDifferentTenant_ShouldSucceed`, `Create_TenantNotResolved_ShouldFail`. |
| 7 | Create a JobTitle with an arbitrary / inactive / cross-tenant GradeId. | Rejected `invalid_grade`. `Create_WithNonExistentGradeId_ShouldFail_Issue021`, `Create_WithInactiveGradeId_ShouldFail_Issue021`, `Create_WithCrossTenantGradeId_ShouldFail_Issue021`. |
| 8 | Create a JobTitle with a valid seeded active GradeId / null GradeId; read the grade name; update grade id to valid / non-existent / removed. | Accepted (null ok); GradeName populated on detail + list; update to valid ok, to non-existent `invalid_grade`, remove ok. `Create_WithValidSeededGradeId_ShouldSucceed_Issue021`, `Create_WithNullGradeId_ShouldSucceed`, `GetById_And_GetAll_PopulateGradeName_Issue021`, `Update_ChangeGradeId_ToValidSeededGrade_ShouldSucceed_Issue021`, `Update_ChangeGradeId_ToNonExistentGrade_ShouldFail_Issue021`, `Update_RemoveGradeId_ShouldSucceed`. |

## 6. Postconditions
- SalaryGrade CRUD enforces per-tenant code uniqueness + amount ordering + tenant isolation; JobTitle grade links only resolve to real active in-tenant grades.

## 7. Test Category Tags
- [x] Happy path (create/update/read grade; valid grade link)
- [x] Negative test (409 dup, 422 range, `invalid_grade` arbitrary/inactive)
- [x] Boundary test (Min/Mid/Max ordering; null mid/grade)
- [ ] Security test
- [x] Multi-tenant isolation (cross-tenant grade 404 / `invalid_grade`; same code across tenants)
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## Automation & Traceability
- **Automated-by (green in the xUnit suite), all carrying `[Trait("TC", "TC-CHR-337")]`:**
  - `HRM.Tests/Unit/SalaryGradeServiceTests.cs` — full SalaryGrade CRUD (all 21 arms: create/trim/null-mid,
    dup-409, range-422, update, deactivate, read/list, cross-tenant isolation, tenant-not-resolved).
  - `HRM.Tests/Unit/JobTitleServiceTests.cs` — the GradeId FK-validation arms (ISSUE-021):
    `Create_WithNonExistentGradeId_ShouldFail_Issue021`, `Create_WithValidSeededGradeId_ShouldSucceed_Issue021`,
    `Create_WithInactiveGradeId_ShouldFail_Issue021`, `Create_WithNullGradeId_ShouldSucceed`,
    `Create_WithCrossTenantGradeId_ShouldFail_Issue021`, `GetById_And_GetAll_PopulateGradeName_Issue021`,
    `Update_ChangeGradeId_ToValidSeededGrade_ShouldSucceed_Issue021`,
    `Update_ChangeGradeId_ToNonExistentGrade_ShouldFail_Issue021`, `Update_RemoveGradeId_ShouldSucceed`.
- These arms pre-existed and are already green; this backfill only adds the `[Trait("TC", "TC-CHR-337")]`
  binding — no test was renamed, weakened, or restructured. **Unblocks TC-CHR-063** (grade-on-profile).
