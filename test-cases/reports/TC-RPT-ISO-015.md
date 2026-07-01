---
id: TC-RPT-ISO-015
user_story: US-RPT-004
module: Reports & Analytics
priority: critical
type: security
status: fail
exec_note: "2026-06-30 API iso-fixture probe: export-DOWNLOAD is isolated (foreign id->404) BUT the report GENERATE/export-source path leaks under foreign X-Tenant-Subdomain (BUG-003, ISSUE-193). Exported DATA scoping fails on the generate side. SignalR per-tenant fan-out not API-probeable. Fail on generate-side leak."
created: 2026-06-17
---

# TC-RPT-ISO-015: Exported DATA is tenant-scoped + permission-scoped; SignalR ready notification only to the owning tenant/user (AC-5, FR-8, BR-1/2, NFR-3)

## 1. Test Objective
Verify that the CONTENTS of an export never leak another tenant's data and respect the report's own
data scoping, and that the async-ready SignalR notification is delivered only to the requesting user
in the owning tenant: (a) an export's rows contain only Tenant A data (no Tenant B rows), (b) the
export honors `View.All` vs `View.Team` scoping (BR-1) and sensitive-field masking without
`ViewSensitive` (BR-2), and (c) the SignalR "ready" push reaches only `t:{tenantA}:user:{hrA}` -- not
Tenant B users. Validates AC-5, FR-8, BR-1, BR-2, NFR-3.

## 2. Related Requirements
- User Story: US-RPT-004
- Acceptance Criteria: AC-5
- Functional Requirements: FR-8 (SignalR notify)
- Business Rules: BR-1 (export respects report scoping View.All/Team), BR-2 (sensitive masking)
- Non-Functional: NFR-3
- Dependencies: US-NTF-001 (SignalR group convention `t:{tenant}:user:{user}`)

> SCOPE NOTE: BR-1's `Reports.View.Team` / `Reports.View.All` split depends on scoped permission
> variants that the catalog does not yet expose (flagged in US-RPT-001). Assert the export reuses the
> SAME data-scoping the report view applies; if the Team/All split is not yet built, assert the export
> matches the view's current scoping and flag the gap rather than relaxing it.

## 3. Preconditions
- Tenant A and Tenant B active with distinct employees. `hrA` (Reports.Export, View.All) and a Team-scoped manager `mgrA`.
- `hrA` lacks `ViewSensitive`; a report containing a sensitive field (e.g. national ID).
- SignalR connections for `hrA` and a Tenant B user `hrB`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A rows | A-only employees | no B rows allowed |
| sensitive field | national_id | masked without ViewSensitive (BR-2) |
| SignalR group | t:{tenantA}:user:{hrA} | only target |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As `hrA`, export a headcount report; inspect the file contents | Only Tenant A employees appear; ZERO Tenant B rows (NFR-3) |
| 2 | As Team-scoped `mgrA`, export the same report | Rows limited to the manager's team scope -- matches the report-view scoping (BR-1) |
| 3 | As `hrA` (no ViewSensitive), export a report including national_id | national_id values are MASKED in the file (BR-2); full values not present |
| 4 | Complete an async export for `hrA`; observe SignalR | Ready push goes to group `t:{tenantA}:user:{hrA}` only |
| 5 | Observe `hrB`'s SignalR channel during step 4 | `hrB` receives NOTHING about A's export (no cross-tenant notification leak, FR-8/BR-5 of US-NTF-001) |
| 6 | Confirm group/tenant derivation | Notification target tenant/user derived server-side, never from client input |

## 6. Postconditions
- Export contents tenant + permission scoped; sensitive masked; ready notification confined to owner.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
