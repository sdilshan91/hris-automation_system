---
id: TC-LV-139
user_story: US-LV-007
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-139: Duplicate tenant-wide (null-location) date prevented (BR-1 boundary, NULL-distinct partial index)

## 1. Test Objective
Verify the BR-1 uniqueness boundary for tenant-wide holidays (LocationId IS NULL): two tenant-wide holidays on the same date are rejected even though SQL treats NULLs as distinct, because a dedicated partial unique index `ix_holiday_tenant_date_nolocation_unique` covers null-location rows (BR-1, Section 7).

## 2. Related Requirements
- User Story: US-LV-007
- Business Rules: BR-1
- Functional Requirements: FR-1
- Data Requirements (Section 7)

## 3. Preconditions
- Tenant "acme" active; an existing tenant-wide (null location) holiday on 2026-10-02.
- HR Officer "Priya" authenticated with `Holiday.Create`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Existing | "National Day", 2026-10-02, null location | tenant-wide |
| Attempt | "Founders Day", 2026-10-02, null location | same date, both tenant-wide |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | POST a second tenant-wide holiday on 2026-10-02 (null location) | 400 Bad Request: "A holiday already exists on 2026-10-02." -- the null-location partial index blocks it. |
| 2 | Verify no duplicate row | Exactly one tenant-wide holiday on 2026-10-02 remains. |
| 3 | POST a location-scoped holiday on 2026-10-02 (loc-NY) | 201 Created -- a location-specific holiday on the same date is allowed (different uniqueness scope). |
| 4 | Confirm the two index scopes are distinct | Tenant-wide uniqueness and per-location uniqueness are enforced by separate partial indexes; neither lets duplicates slip through via NULL-distinctness. |

## 6. Postconditions
- Tenant-wide duplicate-date holidays are rejected; the NULL-distinct gap is closed by the partial index.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
