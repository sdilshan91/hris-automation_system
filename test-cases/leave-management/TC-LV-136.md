---
id: TC-LV-136
user_story: US-LV-007
module: Leave Management
priority: high
type: performance
status: draft
created: 2026-06-14
---

# TC-LV-136: CSV import of 100 rows completes within 5 seconds (NFR-3)

## 1. Test Objective
Verify the CSV import handles up to 100 holiday rows and completes within 5 seconds end-to-end, including validation, duplicate detection, and persistence (NFR-3).

## 2. Related Requirements
- User Story: US-LV-007
- Non-Functional Requirements: NFR-3
- Functional Requirements: FR-4

## 3. Preconditions
- Tenant "acme" active with no existing 2026 holidays; HR Officer authenticated with `Holiday.Import`.
- A valid CSV with exactly 100 distinct-date rows.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Rows | 100 distinct dates | upper bound per NFR-3 |
| Budget | <= 5000 ms | wall-clock |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | POST `/api/v1/holidays/import` with the 100-row CSV and measure wall-clock time | Completes within 5 seconds; createdCount = 100. |
| 2 | Repeat 5 times and record P95 | P95 stays within the 5s budget. |
| 3 | Verify persistence integrity | All 100 rows persisted, tenant-scoped, no partial/duplicate state. |
| 4 | Note size guard | Request size limit (2 MB) comfortably accommodates 100 rows; a 100-row file is well under it. |

## 6. Postconditions
- 100-row import completes within the SLA with all rows persisted.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
