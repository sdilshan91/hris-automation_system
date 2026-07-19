---
id: TC-CHR-335
user_story: US-CHR-002
module: Core HR
priority: high
type: functional
status: automated
created: 2026-07-20
defect:
  - DF-38
  - DF-39
---

# TC-CHR-335: Employee-profile edit persists structured address components and full-replaces the Education / Work-History / Dependents sub-entities (add / update-by-id / remove-omitted), tenant-stamped and tenant-isolated — US-CHR-002 AC-2 / AC-6 / FR-3 (#386, DF-38/DF-39)

## 1. Test Objective
Verify the #386 profile sub-entity backend CRUD (DF-38/DF-39) on US-CHR-002: the profile-edit save path persists and reads back the structured **address columns** (Address, City, State, PostalCode, Country) with the change captured in the ContactInfo audit snapshot (DF-38); and the three profile sub-collections — **Education**, **Work History**, **Dependents** — behave as guarded **full-replace** collections (DF-39): a save with `Update{Section}=true` adds new rows, updates existing rows **by Id**, and **removes omitted** rows, while a save that does NOT set the flag (list null) leaves the section untouched, and an explicit empty list clears it. Every persisted sub-row is **tenant-stamped** (`TenantId == current tenant`), the sub-tables are subject to the EF global query filter so a different tenant's context reads **none** of them, and `GetProfileAsync` returns all three sub-collections. These sections were previously read-only placeholders (no backing entities); #386 added `EmployeeEducation` / `EmployeeWorkHistory` / `EmployeeDependent` tables + the `PATCH {id}/profile` save path.

## 2. Related Requirements
- User Story: US-CHR-002
- Acceptance Criteria: AC-2 (HR edits a profile section → record updated + audit snapshot), AC-6 (employment/profile changes recorded)
- Functional Requirement: FR-3 (field-level permissions on the profile edit path); displayed/edited joined data includes education, work history, dependents
- Business Rule / Note: AC-F1 (#386 — profile sub-entity backend CRUD; sections no longer read-only)
- Findings: DF-38 (structured address components), DF-39 (Education/WorkHistory/Dependents full-replace + tenant isolation)

## 3. Preconditions
- A tenant with a seeded employee (department + job title), created via the real `EmployeeService` over EF Core InMemory-through-real-EF (so the provider-agnostic global query filter is genuinely exercised).
- The caller is an HR Officer (Employee.Edit) for the edit arms; a second, unrelated tenant context is used for the isolation arm.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Address components | Address/City/State/PostalCode/Country = "123 Main St"/"Colombo"/"Western"/"00100"/"Sri Lanka" | DF-38 persist + audit |
| Education (seed → replace) | {MIT (kept by Id, updated), Harvard (dropped), Stanford (added)} | add/update-by-id/remove-omitted |
| WorkHistory (seed → replace) | {Acme (kept by Id), Globex (dropped), Initech (added)} + DateOnly from/to | remove-omitted |
| Dependents (seed → replace) | {Kid One (kept by Id), Kid Two (dropped), Spouse (added)} + nullable DateOnly DOB | remove-omitted |
| Section flags | `UpdateEducation` / `UpdateWorkHistory` / `UpdateDependents` | null flag ⇒ untouched; empty list ⇒ cleared |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Update ContactInfo with all five address components. | Persisted on the employee row and echoed on the DTO; the ContactInfo audit snapshot contains the new value. `UpdateProfile_AddressComponents_ShouldPersistAndReadBack_DF38`. |
| 2 | Seed two Education rows, then full-replace: keep one by Id (updated), drop the other, add a new one. | Result has 2 rows: the kept row updated, the new row present, the dropped row gone; rows are tenant-stamped. `UpdateProfile_Education_FullReplace_AddUpdateRemove_DF39`. |
| 3 | Save Education as an explicit empty list (flag set). | Education cleared. `UpdateProfile_Education_EmptyList_ClearsAll_DF39`. |
| 4 | Save a later update that does NOT set the Education flag (list null). | Existing Education preserved untouched. `UpdateProfile_Education_NullWithoutFlag_LeavesUntouched_DF39`. |
| 5 | Full-replace Work History (add w/ DateOnly; then keep-by-id + drop + add). | Rows persisted with DateOnly from/to; omitted row removed; rows tenant-stamped. `UpdateProfile_WorkHistory_FullReplace_WithDateOnly_DF39`, `UpdateProfile_WorkHistory_FullReplace_RemovesOmitted_DF39`. |
| 6 | Full-replace Dependents (add w/ nullable DateOnly DOB; then keep-by-id + drop + add). | Rows persisted (null DOB allowed); omitted row removed; rows tenant-stamped. `UpdateProfile_Dependents_FullReplace_WithDateOnly_DF39`, `UpdateProfile_Dependents_FullReplace_RemovesOmitted_DF39`. |
| 7 | Seed all three sub-collections under tenant A, then read each sub-table through a DIFFERENT tenant's context. | Each read returns empty — the global query filter isolates the new sub-tables. `SubCollections_AreTenantIsolated_DF39`. |
| 8 | `GetProfileAsync` after seeding all three sub-collections. | Profile returns the Education, WorkHistory and Dependents rows. `GetProfile_ReturnsAllNewSubCollections_DF39`. |

## 6. Postconditions
- Address components persist and audit; the three profile sub-collections behave as tenant-scoped, tenant-isolated full-replace sets driven by their per-section update flags.

## 7. Test Category Tags
- [x] Happy path (address persist; add/update sub-rows)
- [x] Negative test (remove-omitted; empty-clears; null-flag-untouched)
- [x] Boundary test (empty list; nullable DateOnly DOB)
- [ ] Security test
- [x] Multi-tenant isolation (sub-tables read empty across tenants; rows tenant-stamped)
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## Automation & Traceability
- **Automated-by (green in the xUnit suite), all carrying `[Trait("TC", "TC-CHR-335")]` in
  `HRM.Tests/Unit/EmployeeProfileServiceTests.cs`:**
  - DF-38: `UpdateProfile_AddressComponents_ShouldPersistAndReadBack_DF38`.
  - DF-39: `UpdateProfile_Education_FullReplace_AddUpdateRemove_DF39`,
    `UpdateProfile_Education_EmptyList_ClearsAll_DF39`, `UpdateProfile_Education_NullWithoutFlag_LeavesUntouched_DF39`,
    `UpdateProfile_WorkHistory_FullReplace_WithDateOnly_DF39`, `UpdateProfile_WorkHistory_FullReplace_RemovesOmitted_DF39`,
    `UpdateProfile_Dependents_FullReplace_WithDateOnly_DF39`, `UpdateProfile_Dependents_FullReplace_RemovesOmitted_DF39`,
    `SubCollections_AreTenantIsolated_DF39`, `GetProfile_ReturnsAllNewSubCollections_DF39`.
- These arms pre-existed and are already green; this backfill only adds the `[Trait("TC", "TC-CHR-335")]`
  binding — no test was renamed, weakened, or restructured.
