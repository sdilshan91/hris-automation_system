---
id: TC-LV-243
user_story: US-LV-012
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-243: Reports support sorting and server-side pagination (FR-3)

## 1. Test Objective
Verify FR-3: report tables support column sorting and server-side pagination (page, pageSize, totalCount), so large result sets are paged at the database rather than loaded entirely client-side.

## 2. Related Requirements
- User Story: US-LV-012
- Functional Requirements: FR-3, FR-6
- Non-Functional Requirements: NFR-1 (≤2s for ≤1,000 rows)

## 3. Preconditions
- Tenant "acme"; a report with >50 rows so pagination is exercised.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| page / pageSize | 1 / 25 | server-side params |
| sort | e.g. `remaining desc` | sortable column |
| totalCount | reported | for paging UI |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Request page 1 with pageSize 25 | 25 rows returned plus a totalCount; the SQL applies LIMIT/OFFSET (or keyset) server-side, not a full client load. |
| 2 | Request page 2 | The next 25 distinct rows are returned; no overlap/skip with page 1. |
| 3 | Sort by a column ascending then descending | The full ordering is applied server-side across pages (page 1 reflects the global sort order, not just in-page). |
| 4 | Boundary: pageSize beyond max / page beyond last | pageSize is clamped to the documented max; a page past the end returns an empty page with the correct totalCount (no error). |

## 6. Postconditions
- Sorting and server-side pagination behave correctly across pages with a stable totalCount.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
