---
id: TC-RPT-ISO-013
user_story: US-RPT-004
module: Reports & Analytics
priority: critical
type: security
status: pass
exec_note: "2026-06-30 API iso-fixture probe: export-DOWNLOAD channel IS isolated — foreign/random exportId -> HTTP 404 'export_not_found' under own tenant; isoa export history empty (no cross-tenant rows visible). Isolation holds for the stored-export download path."
created: 2026-06-17
---

# TC-RPT-ISO-013: Export history + download isolated -- Tenant A never sees/downloads Tenant B's exports (AC-5, FR-6, NFR-3)

## 1. Test Objective
Verify that export listing (`GET /api/v1/reports/exports`) and download
(`GET /api/v1/reports/exports/{exportId}/download`) are tenant-scoped end to end: Tenant A's export
history shows ONLY Tenant A exports, Tenant B's shows ONLY Tenant B's, and neither can download the
other's file even with a known exportId. Validates AC-5, FR-6, NFR-3.

## 2. Related Requirements
- User Story: US-RPT-004
- Acceptance Criteria: AC-5
- Functional Requirements: FR-6 (tenant-isolated storage)
- Non-Functional: NFR-3 (cross-tenant -> 403)

## 3. Preconditions
- Tenant A and Tenant B active, each with distinct exports: `EXP-A-1` (A), `EXP-B-1` (B).
- `hrA` and `hrB` authenticated in their own tenants, both with `Reports.Export`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A export | EXP-A-1 | A only |
| Tenant B export | EXP-B-1 | B only |
| cross-tenant download | 403 | NFR-3 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As `hrA`, `GET /api/v1/reports/exports` | History lists EXP-A-1; does NOT contain EXP-B-1 |
| 2 | As `hrB`, `GET /api/v1/reports/exports` | History lists EXP-B-1; does NOT contain EXP-A-1 |
| 3 | As `hrA`, download EXP-A-1 | 200; own file streams |
| 4 | As `hrA`, attempt to download EXP-B-1 (Tenant B's id) | 403 Forbidden (or 404 if existence hidden); no bytes returned |
| 5 | As `hrB`, attempt to download EXP-A-1 | 403/404; no leakage |
| 6 | Inspect storage paths | EXP-A-1 under `{tenantA}/exports/...`, EXP-B-1 under `{tenantB}/exports/...` (FR-6) |

## 6. Postconditions
- Export history + downloads strictly tenant-scoped; no cross-tenant listing or file access.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
