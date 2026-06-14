---
id: TC-LV-138
user_story: US-LV-007
module: Leave Management
priority: critical
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-138: Duplicate date + location prevented on create (BR-1, Test Hint)

## 1. Test Objective
Verify that creating a second holiday on the same date for the same location is rejected with a clear error, enforcing the per-(tenant, date, location) uniqueness rule (BR-1, Test Hint §11). Backed by the partial unique index `ix_holiday_tenant_date_location_unique`.

## 2. Related Requirements
- User Story: US-LV-007
- Business Rules: BR-1
- Functional Requirements: FR-1
- Data Requirements (Section 7)

## 3. Preconditions
- Tenant "acme" with location "New York" (loc-NY).
- An existing active holiday "Local Festival" on 2026-08-12 scoped to loc-NY.
- HR Officer "Priya" authenticated with `Holiday.Create`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Existing | "Local Festival", 2026-08-12, loc-NY | already present |
| Attempt | "Another Festival", 2026-08-12, loc-NY | same date + location |
| Different location | "City Day", 2026-08-12, loc-LDN | allowed (different location) |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | POST a second holiday on 2026-08-12 for loc-NY | 400 Bad Request: "A holiday already exists on 2026-08-12 for this location." (BR-1 duplicate prevention). |
| 2 | POST a holiday on 2026-08-12 for loc-LDN (different location) | 201 Created -- same date is allowed for a different location. |
| 3 | Inspect the persisted rows | Exactly one loc-NY holiday on 2026-08-12; no duplicate row created in step 1. |
| 4 | Confirm soft-delete interaction | A previously soft-deleted holiday on the same date+location does NOT block a new create (partial index filters on `is_deleted = false`). |

## 6. Postconditions
- Per-(tenant, date, location) uniqueness is enforced; the same date is allowed across distinct locations.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
