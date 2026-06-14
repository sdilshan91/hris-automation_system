---
id: TC-LV-130
user_story: US-LV-007
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-130: Location-scoped holiday is visible to its location and to a location filter (AC-1)

## 1. Test Objective
Verify that a holiday scoped to a specific location is saved with that `locationId` and that the list endpoint's `locationId` filter returns tenant-wide holidays plus that location's holidays, so employees at the location see it (AC-1, FR-1, FR-2, FR-6).

## 2. Related Requirements
- User Story: US-LV-007
- Acceptance Criteria: AC-1
- Functional Requirements: FR-1, FR-2, FR-6

## 3. Preconditions
- Tenant "acme" with locations "New York" (loc-NY) and "London" (loc-LDN) configured (US-CHR-007).
- HR Officer "Priya" authenticated with `Holiday.Create` / `Holiday.View`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Holiday A | "Thanksgiving", 2026-11-26, Public, loc-NY | location-specific |
| Holiday B | "Spring Bank Holiday", 2026-05-25, Public, null | tenant-wide |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Create Holiday A (loc-NY) and Holiday B (tenant-wide) | Both created; A carries `locationId=loc-NY`, B carries `locationId=null`. |
| 2 | GET `/api/v1/holidays?year=2026&locationId=loc-NY` | Returns BOTH A (NY) and B (tenant-wide). |
| 3 | GET `/api/v1/holidays?year=2026&locationId=loc-LDN` | Returns ONLY B (tenant-wide); A (NY-only) is excluded. |
| 4 | GET `/api/v1/holidays?year=2026` (no location) | Returns A and B (full calendar for the year). |

## 6. Postconditions
- Location-scoped and tenant-wide holidays coexist; the location filter returns the correct subset.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
