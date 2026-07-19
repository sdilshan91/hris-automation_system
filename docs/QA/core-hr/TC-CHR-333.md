---
id: TC-CHR-333
user_story: US-CHR-002
module: Core HR
priority: high
type: functional
status: automated
created: 2026-07-19
automated: 2026-07-19
defect:
  - ISSUE-319
---

# TC-CHR-333: Employee profile-section edits PATCH the single `{id}/profile` endpoint (never the non-existent `sections/:section` route); unbacked sections fire no request (ISSUE-319 / DF-36)

## 1. Test Objective
Verify the ISSUE-319 fix on US-CHR-002 (profile-section editing): the old per-section PATCH `sections/:section` route never existed on the backend, so every inline save 404'd. Profile edits now PATCH the single `{id}/profile` endpoint with an `UpdateEmployeeProfileRequest` body (numeric rowVersion). Sections that have no backing PATCH support must fire **no** HTTP request at all.

## 2. Related Requirements
- User Story: US-CHR-002
- Finding: ISSUE-319 (DF-36) (PR #369 FE follow-up cluster)
- Failure mode: FE calling a route the BE never exposed → all inline saves 404

## 3. Preconditions
- Angular Karma/Jasmine unit tests with `HttpTestingController` (mirrors the service + component specs).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Endpoint | `{baseUrl}/{id}/profile` | the ONLY profile-edit route |
| Method | PATCH | with numeric rowVersion body |
| Legacy route | `sections/:section` | must NEVER be called |

## 5. Test Steps
| Step | Action | Expected Result | Automated by (Karma) |
|------|--------|-----------------|----------------------|
| 1 | Service saves a profile section. | Issues a `PATCH {id}/profile` with a numeric rowVersion; the URL does not contain `/sections/`. | `employee.service.spec.ts` → `it('should PATCH {id}/profile with a numeric rowVersion and return the updated profile')` |
| 2 | Service save URL asserted explicitly. | Calls the `{id}/profile` URL, never `sections/:section`. | `employee.service.spec.ts` → `it('should call the {id}/profile URL (never sections/:section)')` |
| 3 | Component with unbacked sections is submitted. | No PATCH request is fired for the unbacked sections (`httpMock.expectNone(profileUrl)`). | `employee-profile.component.spec.ts` → `it('does NOT fire any PATCH for the unbacked sections')` |

## 6. Postconditions
- Inline profile-section edits hit the real `{id}/profile` endpoint and succeed; unsupported sections make no spurious request.

## 7. Test Category Tags
- [x] Happy path
- [x] Negative test (no request for unbacked sections)
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## Automation & Traceability
- **Automated-by (green in the Angular Karma/Jasmine suite — FE, no xUnit `[Trait]`):**
  - `employee.service.spec.ts` → `it('should PATCH {id}/profile with a numeric rowVersion and return the updated profile')`
  - `employee.service.spec.ts` → `it('should call the {id}/profile URL (never sections/:section)')`
  - `employee-profile.component.spec.ts` → `it('does NOT fire any PATCH for the unbacked sections')`
- FE binding is by spec reference (Karma specs are not tagged with an xUnit `[Trait]`).
